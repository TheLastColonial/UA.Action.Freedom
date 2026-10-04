using Microsoft.Extensions.Logging;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Telemetry;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Manifests;

/// <summary>
/// Move a manifest to <see cref="To"/>. One command for every edge of the diagram; the API
/// gives each its own route.
/// </summary>
public sealed record TransitionManifestCommand(string Id, ManifestStatus To);

public enum TransitionManifestOutcome
{
    Transitioned,
    NotFound,
    IllegalTransition,
    Frozen,
    TruckListNotPublished,
    NotInsured
}

/// <summary>
/// Every move a manifest makes, except confirmation.
/// </summary>
/// <remarks>
/// The legality of a move is not decided here — it is decided by
/// <see cref="ManifestTransitions.CanTransition"/>, which holds the edges of
/// <c>docs/manifest-status.puml</c> as data. This handler adds the two rules the diagram cannot
/// express: a manifest may only be proposed against a convoy whose truck list is published
/// (<c>docs/process.puml</c>), a frozen manifest may only record what happened to the load, and a
/// vehicle may only depart with its insurance recorded, not voided by a crew change, and in cover.
/// </remarks>
public sealed class TransitionManifestHandler(
    IManifestRepository repository,
    IConvoyRepository convoys,
    IConvoyVehicleRepository truckList,
    FreedomMetrics? metrics = null)
    : ICommandHandler<TransitionManifestCommand, TransitionManifestOutcome>
{
    private readonly FreedomMetrics _metrics = metrics ?? FreedomMetrics.Unobserved;

    /// <summary>
    /// The states that would reopen a manifest for editing.
    /// </summary>
    /// <remarks>
    /// §5.2 forbids <em>edits</em> to a manifest whose GMR exists, not progress. Preparing,
    /// Ready, InTransit, Delivered, Lost and Returned all report what is happening to a load
    /// HMRC has already been told about — none of them contradicts the submission, and blocking
    /// them would strand every approved manifest in Confirmed for ever.
    ///
    /// Going back to Proposed or Rejected is different: it would put the manifest back in front
    /// of an approver as something that can still be changed. The state machine happens to make
    /// that unreachable from Confirmed today, so this is a guard against a future edge rather
    /// than a path anything takes now — which is exactly why it is worth stating.
    /// </remarks>
    private static readonly ManifestStatus[] ReopensForEditing =
        [ManifestStatus.Proposed, ManifestStatus.Rejected];

    public async Task<TransitionManifestOutcome> HandleAsync(
        TransitionManifestCommand command, CancellationToken cancellationToken)
    {
        var manifest = await repository.GetByIdAsync(command.Id, cancellationToken);
        var outcome = await Decide(manifest, command, cancellationToken);

        _metrics.ManifestTransition(manifest?.Status, command.To, outcome);

        return outcome;
    }

    private async Task<TransitionManifestOutcome> Decide(
        ManifestReadModel? manifest, TransitionManifestCommand command, CancellationToken cancellationToken)
    {
        if (manifest is null)
        {
            return TransitionManifestOutcome.NotFound;
        }

        if (manifest.Frozen && ReopensForEditing.Contains(command.To))
        {
            return TransitionManifestOutcome.Frozen;
        }

        if (!ManifestTransitions.CanTransition(manifest.Status, command.To))
        {
            return TransitionManifestOutcome.IllegalTransition;
        }

        if (command.To == ManifestStatus.Proposed
            && !await TruckListIsPublished(manifest, cancellationToken))
        {
            return TransitionManifestOutcome.TruckListNotPublished;
        }

        if (command.To == ManifestStatus.InTransit
            && !await IsInsuredToday(manifest, cancellationToken))
        {
            return TransitionManifestOutcome.NotInsured;
        }

        // Conditional on the manifest still being where we found it, so two dispatchers pressing
        // the same button resolve to one transition rather than both reporting success.
        return await repository.TransitionAsync(command.Id, manifest.Status, command.To, cancellationToken)
            ? TransitionManifestOutcome.Transitioned
            : TransitionManifestOutcome.IllegalTransition;
    }

    /// <summary>
    /// The policy names the drivers, so it has to cover every one of them as well as the day the
    /// vehicle leaves.
    /// </summary>
    private async Task<bool> IsInsuredToday(ManifestReadModel manifest, CancellationToken cancellationToken)
    {
        var policy = await truckList.GetInsuranceAsync(manifest.ConvoyId, manifest.Vin, cancellationToken);

        return policy is { CoversAllDrivers: true } && policy.CoversOn(DateTime.UtcNow);
    }

    /// <summary>
    /// A manifest is proposed against the set of vehicles committed to a convoy, so that set has
    /// to be fixed first.
    /// </summary>
    /// <remarks>
    /// The manifest can no longer name a truck that is not on the convoy — the truck-list entry it
    /// belongs to is a foreign key now. What is still worth asking is whether the list has been
    /// closed, because proposing against a list somebody is still adding to is proposing against
    /// nothing in particular.
    /// </remarks>
    private async Task<bool> TruckListIsPublished(ManifestReadModel manifest, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(manifest.ConvoyId, cancellationToken);

        return convoy?.TruckListPublished ?? false;
    }
}

/// <summary>
/// Approve a manifest: confirm it and freeze it. Approval is the Administrator's sign-off of the load
/// and nothing more (ADR 0004, ADR 0006).
/// </summary>
/// <remarks>
/// It no longer hands any paperwork off. The GMR and the French envelope are filed afterwards, as
/// explicit acts on the vehicle's declarations; the document that travels with the vehicle is requested
/// with <see cref="RequestManifestDocumentCommand"/>. A load can still change before it is declared, so
/// declaring is never a by-product of signing off.
/// </remarks>
public sealed record ApproveManifestCommand(string Id);

public sealed class ApproveManifestHandler(
    IManifestRepository repository,
    FreedomMetrics? metrics = null)
    : ICommandHandler<ApproveManifestCommand, TransitionManifestOutcome>
{
    private readonly FreedomMetrics _metrics = metrics ?? FreedomMetrics.Unobserved;

    public async Task<TransitionManifestOutcome> HandleAsync(
        ApproveManifestCommand command, CancellationToken cancellationToken)
    {
        var manifest = await repository.GetByIdAsync(command.Id, cancellationToken);
        var outcome = await Approve(manifest, command, cancellationToken);

        _metrics.ManifestTransition(manifest?.Status, ManifestStatus.Confirmed, outcome);

        return outcome;
    }

    private async Task<TransitionManifestOutcome> Approve(
        ManifestReadModel? manifest, ApproveManifestCommand command, CancellationToken cancellationToken)
    {
        if (manifest is null)
        {
            return TransitionManifestOutcome.NotFound;
        }

        if (manifest.Frozen)
        {
            return TransitionManifestOutcome.Frozen;
        }

        if (!ManifestTransitions.CanTransition(manifest.Status, ManifestStatus.Confirmed))
        {
            return TransitionManifestOutcome.IllegalTransition;
        }

        // Status and the freeze stamp in one statement, so a manifest that is Confirmed but editable
        // never exists (§5.2). It remains the freeze signal until plan 15.
        return await repository.ConfirmAndFreezeAsync(command.Id, manifest.Status, cancellationToken) is null
            ? TransitionManifestOutcome.IllegalTransition
            : TransitionManifestOutcome.Transitioned;
    }
}

/// <summary>Ask for the document that travels with an approved vehicle to be rendered.</summary>
public sealed record RequestManifestDocumentCommand(string Id);

public enum RequestManifestDocumentOutcome
{
    Requested,
    NotFound,
    NotApproved,
}

/// <summary>
/// Composes the travelling document here, where the database is, and puts it on the queue, so the
/// worker that renders it needs no database access and cannot read a delivery address even in principle.
/// </summary>
public sealed class RequestManifestDocumentHandler(
    IManifestRepository repository,
    IManifestWorkQueue queue,
    FreedomMetrics? metrics = null,
    ILogger<RequestManifestDocumentHandler>? logger = null)
    : ICommandHandler<RequestManifestDocumentCommand, RequestManifestDocumentOutcome>
{
    private readonly FreedomMetrics _metrics = metrics ?? FreedomMetrics.Unobserved;

    public async Task<RequestManifestDocumentOutcome> HandleAsync(
        RequestManifestDocumentCommand command, CancellationToken cancellationToken)
    {
        var manifest = await repository.GetByIdAsync(command.Id, cancellationToken);

        if (manifest is null)
        {
            return RequestManifestDocumentOutcome.NotFound;
        }

        // The document describes a signed-off load; before approval the load can still change.
        if (!manifest.Frozen)
        {
            return RequestManifestDocumentOutcome.NotApproved;
        }

        try
        {
            await queue.EnqueueDocumentAsync(await ComposeDocument(command.Id, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _metrics.ApproveFailedAfterFreeze("document");
            logger?.LogWarning(
                "Manifest {ManifestId} could not enqueue its document ({ExceptionType}); an operator must retry it.",
                command.Id, exception.GetType().Name);

            throw;
        }

        return RequestManifestDocumentOutcome.Requested;
    }

    private async Task<ManifestDocumentRequest> ComposeDocument(string id, CancellationToken cancellationToken)
    {
        // What the border reads off the front of the vehicle: the plate, not the chassis number.
        var plate = await repository.GetVehiclePlateAsync(id, cancellationToken) ?? string.Empty;
        var vehicleKg = await repository.GetVehicleWeightKgAsync(id, cancellationToken);
        var lines = await repository.GetDocumentLinesAsync(id, cancellationToken);
        var cargoKg = lines.Sum(line => line.WeightKg);

        return new ManifestDocumentRequest(
            id,
            plate,
            vehicleKg,
            cargoKg,
            ManifestWeight.CrewAndBagsKg,
            ManifestWeight.FuelKg,
            ManifestWeight.Total(vehicleKg, cargoKg),
            lines);
    }
}
