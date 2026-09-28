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
    NotInsured,

    /// <summary>
    /// Approval only: no ICS2 Entry Summary Declaration has been recorded for this manifest, so the
    /// French logistics envelope it would ask for has no formality to name (ENV_CTR_RG08).
    /// </summary>
    EnsNotFiled
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
    /// The policy names the crew, so it has to be recorded after the last crew change — a change
    /// voids it — and cover the day the vehicle leaves.
    /// </summary>
    private async Task<bool> IsInsuredToday(ManifestReadModel manifest, CancellationToken cancellationToken)
    {
        var policy = await truckList.GetInsuranceAsync(manifest.ConvoyId, manifest.Vin, cancellationToken);

        return policy?.CoversOn(DateTime.UtcNow) ?? false;
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
/// Approve a manifest: confirm it, freeze it, and hand its border paperwork off — the UK Goods
/// Movement Reference, the document that travels with the vehicle, and the French logistics
/// envelope.
/// </summary>
/// <remarks>
/// This is the fork in <c>docs/process.puml</c> — approval is what releases the paperwork — and
/// it is the moment a manifest stops being editable.
///
/// <para>
/// It is also the one gate that will not open without an ICS2 Entry Summary Declaration. That check
/// happens <em>before</em> the freeze, unlike everything else here, because the alternative is a
/// manifest frozen for ever against an envelope French customs will never issue — which is what the
/// placeholder declaration identifier used to produce (<c>docs/adr/0003</c>).
/// </para>
/// </remarks>
public sealed record ApproveManifestCommand(string Id);

public sealed class ApproveManifestHandler(
    IManifestRepository repository,
    IConvoyRepository convoys,
    IManifestWorkQueue queue,
    IEnsDeclarationStore declarations,
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

        // Read the declaration before anything is written. Under ENV_CTR_RG08 a loaded TIR/ATA lorry's
        // envelope must name exactly one formality, and the ENS is it — so approving without one
        // would freeze the manifest against an envelope French customs refuses with FONC-ERR-004.
        // Refusing here leaves the manifest exactly as it was, approvable again once the MRN arrives.
        var profile = EloCrossingProfile.HumanitarianAidToUkraine;
        var declaration = await declarations.GetAsync(command.Id, cancellationToken);

        if (profile.RequiresADeclaration && declaration is null)
        {
            return TransitionManifestOutcome.EnsNotFiled;
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
        // What the border reads off the front of the vehicle, not the chassis number. Both the GMR
        // submission and the printed document were being handed the VIN despite both saying
        // "registration"; the plate lives on dbo.Vehicle and was never read.
        var plate = await repository.GetVehiclePlateAsync(command.Id, cancellationToken) ?? string.Empty;

        await HandOff("gmr", command.Id, async () =>
        {
            // HMRC needs a crossing time and the convoy is what knows it.
            var convoy = await convoys.GetByIdAsync(manifest.ConvoyId, cancellationToken);

            // The message carries the reference, the plate and the departure. No receiver, no
            // address: the worker talks to HMRC, and where in Ukraine the load is going is none of
            // its business — and a queue message is durable and widely readable (§4.4). The ENS MRN
            // is not here either, for a reason GmrSubmissionRequest states.
            await queue.EnqueueGmrSubmissionAsync(
                new GmrSubmissionRequest(command.Id, plate, convoy?.Start),
                cancellationToken);
        });

        // The other half of the fork in docs/process.puml: the document that travels with the
        // vehicle. Composed here, where the database is, so the worker that renders it needs no
        // database access — and therefore cannot read a delivery address even in principle.
        await HandOff("document", command.Id, async () =>
            await queue.EnqueueDocumentAsync(
                await ComposeDocument(command.Id, plate, cancellationToken), cancellationToken));

        // The third prong: France requires a logistics envelope per transport unit at the Smart
        // Border, and approval is what releases it (docs/process.puml). The envelope says nothing
        // about the load — only which way the lorry is crossing, under what regime, and which
        // formalities it is being paired to — so there is nothing here to compose and nothing to
        // withhold. The identifiers are a list because the envelope's field is; under TIR/ATA it
        // holds exactly the one ENS, and the guard above is what guarantees there is one.
        await HandOff("elo", command.Id, async () =>
            await queue.EnqueueEloEnvelopeAsync(
                new EloEnvelopeRequest(
                    command.Id,
                    profile,
                    declaration is null ? [] : [declaration.Mrn]),
                cancellationToken));

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

    private async Task<ManifestDocumentRequest> ComposeDocument(
        string id, string plate, CancellationToken cancellationToken)
    {
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
