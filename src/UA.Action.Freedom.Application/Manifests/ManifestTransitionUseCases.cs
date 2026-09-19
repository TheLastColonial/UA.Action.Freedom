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
    /// The policy names the crew, so it has to be recorded after the last crew change — a change
    /// voids it — and cover the day the vehicle leaves.
    /// </summary>
    private async Task<bool> IsInsuredToday(ManifestReadModel manifest, CancellationToken cancellationToken)
    {
        if (manifest is not { ConvoyId: { } convoyId, Vin: { } vin })
        {
            return false;
        }

        var policy = await convoys.GetInsuranceAsync(convoyId, vin, cancellationToken);

        return policy?.CoversOn(DateTime.UtcNow) ?? false;
    }

    /// <summary>
    /// A manifest is proposed against the set of vehicles committed to a convoy, so that set has
    /// to be fixed first. Without this, a manifest could name a truck that later left the convoy.
    /// </summary>
    private async Task<bool> TruckListIsPublished(ManifestReadModel manifest, CancellationToken cancellationToken)
    {
        if (manifest.ConvoyId is not { } convoyId)
        {
            return false;
        }

        var convoy = await convoys.GetByIdAsync(convoyId, cancellationToken);

        return convoy?.TruckListPublished ?? false;
    }
}

/// <summary>
/// Approve a manifest: confirm it, freeze it, and hand its Goods Movement Reference to the
/// customs worker.
/// </summary>
/// <remarks>
/// This is the fork in <c>docs/process.puml</c> — approval is what releases the paperwork — and
/// it is the moment a manifest stops being editable.
/// </remarks>
public sealed record ApproveManifestCommand(string Id);

public sealed class ApproveManifestHandler(
    IManifestRepository repository,
    IConvoyRepository convoys,
    IManifestWorkQueue queue,
    FreedomMetrics? metrics = null,
    ILogger<ApproveManifestHandler>? logger = null)
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

        // Freeze first, enqueue second, and deliberately in that order. If the enqueue fails the
        // manifest is frozen with no GMR — visible, and an operator can retry the submission.
        // The other order risks an unfrozen manifest whose GMR is already on its way, which is
        // precisely what §5.2 rules out.
        if (await repository.ConfirmAndFreezeAsync(command.Id, manifest.Status, cancellationToken) is null)
        {
            return TransitionManifestOutcome.IllegalTransition;
        }

        // From here the manifest is frozen. A failure in either hand-off below leaves it frozen
        // with paperwork that will never be produced — visible and retryable, but only if someone
        // is told, so each is counted and logged before it propagates.
        await HandOff("gmr", command.Id, async () =>
        {
            // HMRC needs a crossing time and the convoy is what knows it.
            var convoy = manifest.ConvoyId is { } convoyId
                ? await convoys.GetByIdAsync(convoyId, cancellationToken)
                : null;

            // The message carries the reference, the plate and the departure. No receiver, no
            // address: the worker talks to HMRC, and where in Ukraine the load is going is none of
            // its business — and a queue message is durable and widely readable (§4.4).
            await queue.EnqueueGmrSubmissionAsync(
                new GmrSubmissionRequest(command.Id, manifest.Vin ?? string.Empty, convoy?.Start),
                cancellationToken);
        });

        // The other half of the fork in docs/process.puml: the document that travels with the
        // vehicle. Composed here, where the database is, so the worker that renders it needs no
        // database access — and therefore cannot read a delivery address even in principle.
        await HandOff("document", command.Id, async () =>
            await queue.EnqueueDocumentAsync(
                await ComposeDocument(command.Id, manifest, cancellationToken), cancellationToken));

        return TransitionManifestOutcome.Transitioned;
    }

    private async Task HandOff(string stage, string manifestId, Func<Task> handOff)
    {
        try
        {
            await handOff();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _metrics.ApproveFailedAfterFreeze(stage);
            logger?.LogWarning(
                "Manifest {ManifestId} was frozen but its {Stage} hand-off failed ({ExceptionType}); an operator must retry it.",
                manifestId, stage, exception.GetType().Name);

            throw;
        }
    }

    /// <summary>Two drivers and their bags. A border-check estimate, deliberately fixed.</summary>
    private const int CrewAndBagsKg = 100 * 2;

    /// <summary>Fuel allowance. Also deliberately fixed.</summary>
    private const int FuelKg = 45;

    private async Task<ManifestDocumentRequest> ComposeDocument(
        string id, ManifestReadModel manifest, CancellationToken cancellationToken)
    {
        var vehicleKg = await repository.GetVehicleWeightKgAsync(id, cancellationToken);
        var lines = await repository.GetDocumentLinesAsync(id, cancellationToken);
        var cargoKg = lines.Sum(line => line.WeightKg);

        return new ManifestDocumentRequest(
            id,
            manifest.Vin,
            vehicleKg,
            cargoKg,
            CrewAndBagsKg,
            FuelKg,
            vehicleKg + cargoKg + CrewAndBagsKg + FuelKg,
            lines);
    }
}
