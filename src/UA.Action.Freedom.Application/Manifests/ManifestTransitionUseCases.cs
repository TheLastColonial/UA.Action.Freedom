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
    TruckListNotPublished
}

/// <summary>
/// Proposing and rejecting a manifest. Approval has its own handler.
/// </summary>
/// <remarks>
/// The legality of a move is decided by <see cref="ManifestTransitions.CanBeRequested"/>, which holds the edges of
/// <c>docs/manifest-status.puml</c> as data and leaves out the one edge only a load change takes (Approved to
/// Proposed). This handler adds the rule the diagram cannot express: a manifest may only be proposed against a convoy
/// whose truck list is published (<c>docs/process.puml</c>).
/// </remarks>
public sealed class TransitionManifestHandler(
    IManifestRepository repository,
    IConvoyRepository convoys,
    FreedomMetrics? metrics = null)
    : ICommandHandler<TransitionManifestCommand, TransitionManifestOutcome>
{
    private readonly FreedomMetrics _metrics = metrics ?? FreedomMetrics.Unobserved;

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

        if (!ManifestTransitions.CanBeRequested(manifest.Status, command.To))
        {
            return TransitionManifestOutcome.IllegalTransition;
        }

        if (command.To == ManifestStatus.Proposed
            && !await TruckListIsPublished(manifest, cancellationToken))
        {
            return TransitionManifestOutcome.TruckListNotPublished;
        }

        // Conditional on the manifest still being where we found it, so two dispatchers pressing
        // the same button resolve to one transition rather than both reporting success.
        return await repository.TransitionAsync(command.Id, manifest.Status, command.To, cancellationToken)
            ? TransitionManifestOutcome.Transitioned
            : TransitionManifestOutcome.IllegalTransition;
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
/// Approve a manifest. Approval is the Administrator's sign-off of the load and nothing more (ADR 0004, ADR 0006).
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

        _metrics.ManifestTransition(manifest?.Status, ManifestStatus.Approved, outcome);

        return outcome;
    }

    private async Task<TransitionManifestOutcome> Approve(
        ManifestReadModel? manifest, ApproveManifestCommand command, CancellationToken cancellationToken)
    {
        if (manifest is null)
        {
            return TransitionManifestOutcome.NotFound;
        }

        if (!ManifestTransitions.CanBeRequested(manifest.Status, ManifestStatus.Approved))
        {
            return TransitionManifestOutcome.IllegalTransition;
        }

        // Conditional on the manifest still being Proposed, so two approvers resolve to one approval.
        return await repository.ApproveAsync(command.Id, cancellationToken)
            ? TransitionManifestOutcome.Transitioned
            : TransitionManifestOutcome.IllegalTransition;
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
        if (!manifest.Approved)
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
