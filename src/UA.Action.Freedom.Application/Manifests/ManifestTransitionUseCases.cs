using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
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
    TruckListNotPublished
}

/// <summary>
/// Every move a manifest makes, except confirmation.
/// </summary>
/// <remarks>
/// The legality of a move is not decided here — it is decided by
/// <see cref="ManifestTransitions.CanTransition"/>, which holds the edges of
/// <c>docs/manifest-status.puml</c> as data. This handler adds the two rules the diagram cannot
/// express: a manifest may only be proposed against a convoy whose truck list is published
/// (<c>docs/process.puml</c>), and a frozen manifest may only record what happened to the load.
/// </remarks>
public sealed class TransitionManifestHandler(
    IManifestRepository repository,
    IConvoyRepository convoys)
    : ICommandHandler<TransitionManifestCommand, TransitionManifestOutcome>
{
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

        // Conditional on the manifest still being where we found it, so two dispatchers pressing
        // the same button resolve to one transition rather than both reporting success.
        return await repository.TransitionAsync(command.Id, manifest.Status, command.To, cancellationToken)
            ? TransitionManifestOutcome.Transitioned
            : TransitionManifestOutcome.IllegalTransition;
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
    IManifestWorkQueue queue)
    : ICommandHandler<ApproveManifestCommand, TransitionManifestOutcome>
{
    public async Task<TransitionManifestOutcome> HandleAsync(
        ApproveManifestCommand command, CancellationToken cancellationToken)
    {
        var manifest = await repository.GetByIdAsync(command.Id, cancellationToken);

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

        return TransitionManifestOutcome.Transitioned;
    }
}
