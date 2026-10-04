using Microsoft.Extensions.Logging;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.Telemetry;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Declarations;

/// <summary>How an authority's declaration is filed (ADR 0006). Manual is the default everywhere.</summary>
public enum SubmissionMode
{
    /// <summary>A Dispatcher files by hand in the authority's portal and records the reference.</summary>
    Manual = 0,

    /// <summary>The system enqueues the submission and the worker answers.</summary>
    Automatic = 1,
}

/// <summary>
/// The submission mode per authority that has a client. The ENS and the Ukrainian goods list have no
/// entry because they can never be automatic (ADR 0003, D20).
/// </summary>
public sealed record DeclarationSubmissionModes(
    SubmissionMode Gmr = SubmissionMode.Manual,
    SubmissionMode Elo = SubmissionMode.Manual)
{
    public SubmissionMode For(DeclarationKind kind) => kind switch
    {
        DeclarationKind.Gmr => this.Gmr,
        DeclarationKind.Elo => this.Elo,
        _ => SubmissionMode.Manual,
    };
}

/// <summary>
/// The bounded reasons a declaration may be recorded as refused for. The authority's own text is never
/// kept: it can quote the declaration it objected to.
/// </summary>
public static class DeclarationRefusalReasons
{
    public static readonly IReadOnlyList<string> Codes =
        ["data-error", "goods-mismatch", "missing-document", "technical", "other"];

    public static bool IsKnown(string? code) => code is not null && Codes.Contains(code);
}

/// <summary>Every declaration on a vehicle's truck-list entry, or null when it is not on the convoy.</summary>
public sealed record ListDeclarationsQuery(int ConvoyId, string Vin);

public sealed class ListDeclarationsHandler(
    IConvoyVehicleRepository truckList, IDeclarationRepository declarations, IVehicleLoadReader loads)
    : IQueryHandler<ListDeclarationsQuery, IReadOnlyList<DeclarationReadModel>?>
{
    public async Task<IReadOnlyList<DeclarationReadModel>?> HandleAsync(
        ListDeclarationsQuery query, CancellationToken cancellationToken)
    {
        if (await truckList.GetAsync(query.ConvoyId, query.Vin, cancellationToken) is null)
        {
            return null;
        }

        var stored = await declarations.ListAsync(query.ConvoyId, query.Vin, cancellationToken);
        var current = await loads.ReadAsync(query.ConvoyId, query.Vin, cancellationToken);

        return [.. stored.Select(declaration => DeclarationStaleness.Derive(declaration, current))];
    }
}

/// <summary>
/// Record the reference the authority issued, filing by hand. <see cref="ReceiverRef"/> names the
/// receiver for a goods list and must be absent for every other kind.
/// </summary>
public sealed record RecordDeclarationCommand(
    int ConvoyId, string Vin, DeclarationKind Kind, string Reference, Guid? ReceiverRef = null);

public enum RecordDeclarationOutcome
{
    Recorded,
    VehicleNotOnConvoy,
    AlreadyRecorded,
    ReceiverRequired,
    ReceiverNotAllowed,

    /// <summary>The ENS carries more than a reference, so it has its own route.</summary>
    UseEnsRoute,

    /// <summary>An ELO is created from an accepted ENS, so it cannot be recorded before one.</summary>
    EnsNotAccepted,
}

public sealed class RecordDeclarationHandler(IDeclarationRepository declarations, IDeclarationSnapshots snapshots)
    : ICommandHandler<RecordDeclarationCommand, RecordDeclarationOutcome>
{
    public async Task<RecordDeclarationOutcome> HandleAsync(
        RecordDeclarationCommand command, CancellationToken cancellationToken)
    {
        if (command.Kind == DeclarationKind.Ens)
        {
            return RecordDeclarationOutcome.UseEnsRoute;
        }

        if (command.Kind == DeclarationKind.GoodsList && command.ReceiverRef is null)
        {
            return RecordDeclarationOutcome.ReceiverRequired;
        }

        if (command.Kind != DeclarationKind.GoodsList && command.ReceiverRef is not null)
        {
            return RecordDeclarationOutcome.ReceiverNotAllowed;
        }

        if (command.Kind == DeclarationKind.Elo
            && !await EnsAccepted.IsAsync(declarations, command.ConvoyId, command.Vin, cancellationToken))
        {
            return RecordDeclarationOutcome.EnsNotAccepted;
        }

        var recorded = await declarations.RecordReferenceAsync(
            command.ConvoyId, command.Vin, command.Kind, command.ReceiverRef, command.Reference, cancellationToken);

        if (recorded != RecordReferenceResult.Recorded)
        {
            return recorded == RecordReferenceResult.VehicleNotOnConvoy
                ? RecordDeclarationOutcome.VehicleNotOnConvoy
                : RecordDeclarationOutcome.AlreadyRecorded;
        }

        await snapshots.StampAsync(command.ConvoyId, command.Vin, command.Kind, command.ReceiverRef, cancellationToken);
        return RecordDeclarationOutcome.Recorded;
    }
}

/// <summary>Record that the authority refused a filed declaration, with a bounded reason code only.</summary>
public sealed record RefuseDeclarationCommand(
    int ConvoyId, string Vin, DeclarationKind Kind, string ReasonCode, Guid? ReceiverRef = null);

public enum RefuseDeclarationOutcome
{
    Refused,
    NotFound,
    NotFiled,
    UnknownReason,
}

public sealed class RefuseDeclarationHandler(IDeclarationRepository declarations)
    : ICommandHandler<RefuseDeclarationCommand, RefuseDeclarationOutcome>
{
    public async Task<RefuseDeclarationOutcome> HandleAsync(
        RefuseDeclarationCommand command, CancellationToken cancellationToken)
    {
        if (!DeclarationRefusalReasons.IsKnown(command.ReasonCode))
        {
            return RefuseDeclarationOutcome.UnknownReason;
        }

        var current = await declarations.GetCurrentAsync(
            command.ConvoyId, command.Vin, command.Kind, command.ReceiverRef, cancellationToken);

        if (current is null)
        {
            return RefuseDeclarationOutcome.NotFound;
        }

        if (!DeclarationTransitions.CanTransition(current.Status, DeclarationStatus.Refused))
        {
            return RefuseDeclarationOutcome.NotFiled;
        }

        return await declarations.RefuseAsync(current.Id, command.ReasonCode, cancellationToken)
            ? RefuseDeclarationOutcome.Refused
            : RefuseDeclarationOutcome.NotFiled;
    }
}

/// <summary>
/// File a declaration through its worker. Only the GMR and the ELO have one, and only in
/// <see cref="SubmissionMode.Automatic"/>; in manual mode the answer is to record the reference instead.
/// </summary>
public sealed record FileDeclarationCommand(int ConvoyId, string Vin, DeclarationKind Kind);

public enum FileDeclarationOutcome
{
    Filed,
    VehicleNotOnConvoy,
    NotAutomatable,
    ManualMode,
    ManifestNotApproved,
    EnsNotAccepted,
    AlreadyFiled,
}

/// <summary>
/// Enqueues what approval used to enqueue as a side effect (ADR 0006). The order is the old one: the
/// message goes first and the declaration is stamped <see cref="DeclarationStatus.Filed"/> after it,
/// because the workers have no database — "filed" in automatic mode means enqueued.
/// </summary>
public sealed class FileDeclarationHandler(
    IDeclarationRepository declarations,
    IManifestRepository manifests,
    IConvoyRepository convoys,
    IManifestWorkQueue queue,
    DeclarationSubmissionModes modes,
    IDeclarationSnapshots snapshots,
    FreedomMetrics? metrics = null,
    ILogger<FileDeclarationHandler>? logger = null)
    : ICommandHandler<FileDeclarationCommand, FileDeclarationOutcome>
{
    private readonly FreedomMetrics _metrics = metrics ?? FreedomMetrics.Unobserved;

    public async Task<FileDeclarationOutcome> HandleAsync(
        FileDeclarationCommand command, CancellationToken cancellationToken)
    {
        if (command.Kind is not (DeclarationKind.Gmr or DeclarationKind.Elo))
        {
            return FileDeclarationOutcome.NotAutomatable;
        }

        if (modes.For(command.Kind) != SubmissionMode.Automatic)
        {
            return FileDeclarationOutcome.ManualMode;
        }

        var manifest = await manifests.GetForVehicleAsync(command.ConvoyId, command.Vin, cancellationToken);

        if (manifest is null)
        {
            return FileDeclarationOutcome.VehicleNotOnConvoy;
        }

        // Declarations are prepared after sign-off (ADR 0006): an unapproved load can still change.
        if (!manifest.Frozen)
        {
            return FileDeclarationOutcome.ManifestNotApproved;
        }

        var current = await declarations.GetCurrentAsync(
            command.ConvoyId, command.Vin, command.Kind, null, cancellationToken);

        if (current is not null && current.Status != DeclarationStatus.Draft)
        {
            return FileDeclarationOutcome.AlreadyFiled;
        }

        var ens = command.Kind == DeclarationKind.Elo
            ? await declarations.GetCurrentAsync(command.ConvoyId, command.Vin, DeclarationKind.Ens, null, cancellationToken)
            : null;

        if (command.Kind == DeclarationKind.Elo && ens is not { Status: DeclarationStatus.Accepted, Reference: not null })
        {
            return FileDeclarationOutcome.EnsNotAccepted;
        }

        await HandOff(command.Kind, manifest.Id, async () => await EnqueueAsync(command, manifest, ens, cancellationToken));

        var result = await declarations.RecordReferenceAsync(
            command.ConvoyId, command.Vin, command.Kind, null, null, cancellationToken);

        if (result != RecordReferenceResult.Recorded)
        {
            return FileDeclarationOutcome.AlreadyFiled;
        }

        await snapshots.StampAsync(command.ConvoyId, command.Vin, command.Kind, null, cancellationToken);
        return FileDeclarationOutcome.Filed;
    }

    private async Task EnqueueAsync(
        FileDeclarationCommand command, ManifestReadModel manifest, DeclarationReadModel? ens,
        CancellationToken cancellationToken)
    {
        if (command.Kind == DeclarationKind.Gmr)
        {
            var plate = await manifests.GetVehiclePlateAsync(manifest.Id, cancellationToken) ?? string.Empty;
            var convoy = await convoys.GetByIdAsync(manifest.ConvoyId, cancellationToken);

            // No receiver, no address: the worker talks to HMRC and a queue message is durable and widely
            // readable. The ENS MRN is not here either, for the reason GmrSubmissionRequest states.
            await queue.EnqueueGmrSubmissionAsync(
                new GmrSubmissionRequest(manifest.Id, plate, convoy?.Start), cancellationToken);
            return;
        }

        // The envelope names only a crossing profile and the ENS MRN recorded against the vehicle.
        await queue.EnqueueEloEnvelopeAsync(
            new EloEnvelopeRequest(
                manifest.Id,
                EloCrossingProfile.HumanitarianAidToUkraine,
                ens?.Reference is { } mrn ? [mrn] : []),
            cancellationToken);
    }

    private async Task HandOff(DeclarationKind kind, string manifestId, Func<Task> handOff)
    {
        try
        {
            await handOff();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _metrics.ApproveFailedAfterFreeze(kind.ToString().ToLowerInvariant());
            logger?.LogWarning(
                "Manifest {ManifestId} could not enqueue its {Stage} declaration ({ExceptionType}); an operator must retry it.",
                manifestId, kind, exception.GetType().Name);

            throw;
        }
    }
}

/// <summary>The one answer to "has this vehicle an accepted ENS?", which the ELO is created from.</summary>
internal static class EnsAccepted
{
    public static async Task<bool> IsAsync(
        IDeclarationRepository declarations, int convoyId, string vin, CancellationToken cancellationToken) =>
        await declarations.GetCurrentAsync(convoyId, vin, DeclarationKind.Ens, null, cancellationToken)
            is { Status: DeclarationStatus.Accepted };
}
