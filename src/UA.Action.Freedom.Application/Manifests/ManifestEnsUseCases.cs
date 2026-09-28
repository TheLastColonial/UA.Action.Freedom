using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Manifests;

/// <summary>
/// The ICS2 Entry Summary Declaration this manifest's crossing was accepted under.
/// </summary>
/// <remarks>
/// A reference and who obtained it, and nothing about the load. An ENS describes a consignment in
/// detail, but Freedom never holds that description: it is composed for a filing sheet, read once,
/// and what comes back is the MRN alone. So this record has nowhere to put a receiver, an address or
/// a contact — the same structural redaction as <see cref="ManifestDocumentRequest"/> and
/// <see cref="EloEnvelopeRequest"/>, and a unit test pins it.
/// </remarks>
/// <param name="ManifestId">Which manifest's crossing this declaration covers.</param>
/// <param name="Mrn">The Movement Reference Number ICS2 issued, eighteen characters.</param>
/// <param name="AcceptedAt">When ICS2 accepted it — the authority's timestamp, not ours.</param>
/// <param name="FiledBy">
/// Who filed it. A name or portal login rather than a <c>PersonId</c>: the filer holds a Ground
/// Officer's account at the EU Customs Trader Portal, which is not something Freedom administers.
/// </param>
/// <param name="FilingReference">
/// The filer's own reference for the submission, if they kept one. Useful for finding the filing
/// again in the portal; nothing depends on it.
/// </param>
public sealed record EnsDeclarationReadModel(
    string ManifestId,
    string Mrn,
    DateTimeOffset AcceptedAt,
    string FiledBy,
    string? FilingReference);

/// <summary>
/// Where the recorded declarations live.
/// </summary>
/// <remarks>
/// Blob storage, beside the envelope the MRN ends up inside, rather than a column on
/// <c>dbo.Manifest</c> — the same shape as <see cref="IEloEnvelopeStore"/>, and for the same reason:
/// this is border paperwork about a manifest rather than part of what a manifest is.
///
/// <para>
/// <see cref="SaveAsync"/> creates and never replaces, and says which happened rather than throwing.
/// That is the write-once discipline <c>Manifest.GmrSubmittedAt</c> gets from a conditional
/// <c>UPDATE</c>, carried into storage: the adapter uses a conditional create, so the race between
/// two dispatchers is settled where both writes are visible and not by a read-then-write here.
/// </para>
/// </remarks>
public interface IEnsDeclarationStore
{
    Task<EnsDeclarationReadModel?> GetAsync(string manifestId, CancellationToken cancellationToken);

    /// <summary>
    /// Records a declaration. Returns <see langword="false"/>, without replacing anything, when this
    /// manifest already has one.
    /// </summary>
    Task<bool> SaveAsync(EnsDeclarationReadModel declaration, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the current declaration aside so a refiled one can be recorded, keeping the old one.
    /// Returns <see langword="false"/> when there was none.
    /// </summary>
    /// <remarks>
    /// Several ENS fields are non-amendable, so a mistake in one is corrected by invalidating the
    /// declaration in ICS2 and filing a new one. The superseded reference is kept because it is what
    /// a customs query months later will be about.
    /// </remarks>
    Task<bool> SupersedeAsync(string manifestId, CancellationToken cancellationToken);
}

/// <summary>Record the MRN ICS2 issued for this manifest's crossing.</summary>
/// <remarks>
/// A <c>PUT</c> rather than a <c>POST</c> because there is at most one declaration per manifest, and
/// the manifest is what names it.
/// </remarks>
public sealed record RecordEnsDeclarationCommand(
    string ManifestId,
    string Mrn,
    DateTimeOffset AcceptedAt,
    string FiledBy,
    string? FilingReference = null);

public enum RecordEnsOutcome
{
    Recorded,
    ManifestNotFound,
    AlreadyRecorded,
    MalformedMrn,
    ManifestFrozen,
}

public sealed class RecordEnsDeclarationHandler(
    IManifestRepository repository, IEnsDeclarationStore declarations)
    : ICommandHandler<RecordEnsDeclarationCommand, RecordEnsOutcome>
{
    public async Task<RecordEnsOutcome> HandleAsync(
        RecordEnsDeclarationCommand command, CancellationToken cancellationToken)
    {
        // The shape is checked before the manifest is read: a malformed MRN is the caller's mistake
        // whatever the manifest turns out to be, and one recorded here becomes an envelope French
        // customs refuses with FONC-ERR-004 once the manifest is frozen and the convoy is loading.
        if (!EnsMrn.IsWellFormed(command.Mrn))
        {
            return RecordEnsOutcome.MalformedMrn;
        }

        var manifest = await repository.GetByIdAsync(command.ManifestId, cancellationToken);

        if (manifest is null)
        {
            return RecordEnsOutcome.ManifestNotFound;
        }

        // A frozen manifest has already asked for its envelope, naming whatever formalities existed
        // then. A declaration recorded now would be a reference nothing reads.
        if (manifest.Frozen)
        {
            return RecordEnsOutcome.ManifestFrozen;
        }

        var declaration = new EnsDeclarationReadModel(
            command.ManifestId,
            command.Mrn,
            command.AcceptedAt,
            command.FiledBy,
            command.FilingReference);

        return await declarations.SaveAsync(declaration, cancellationToken)
            ? RecordEnsOutcome.Recorded
            : RecordEnsOutcome.AlreadyRecorded;
    }
}

/// <summary>
/// Set this manifest's declaration aside, because it was invalidated in ICS2 and will be refiled.
/// </summary>
public sealed record SupersedeEnsDeclarationCommand(string ManifestId);

public enum SupersedeEnsOutcome
{
    Superseded,
    ManifestNotFound,
    NotRecorded,
    ManifestFrozen,
}

public sealed class SupersedeEnsDeclarationHandler(
    IManifestRepository repository, IEnsDeclarationStore declarations)
    : ICommandHandler<SupersedeEnsDeclarationCommand, SupersedeEnsOutcome>
{
    public async Task<SupersedeEnsOutcome> HandleAsync(
        SupersedeEnsDeclarationCommand command, CancellationToken cancellationToken)
    {
        var manifest = await repository.GetByIdAsync(command.ManifestId, cancellationToken);

        if (manifest is null)
        {
            return SupersedeEnsOutcome.ManifestNotFound;
        }

        // The envelope already names this MRN. Withdrawing it would leave French customs pairing a
        // crossing against a formality Freedom no longer believes in.
        if (manifest.Frozen)
        {
            return SupersedeEnsOutcome.ManifestFrozen;
        }

        return await declarations.SupersedeAsync(command.ManifestId, cancellationToken)
            ? SupersedeEnsOutcome.Superseded
            : SupersedeEnsOutcome.NotRecorded;
    }
}

/// <summary>Has this manifest's crossing got an accepted ENS yet?</summary>
public sealed record GetManifestEnsQuery(string Id);

public sealed class GetManifestEnsHandler(IEnsDeclarationStore declarations)
    : IQueryHandler<GetManifestEnsQuery, EnsDeclarationReadModel?>
{
    // Deliberately does not check that the manifest exists first, following GetManifestEloHandler:
    // both "no such manifest" and "nothing recorded yet" are a 404 to the caller, so the extra round
    // trip could not change the answer.
    public Task<EnsDeclarationReadModel?> HandleAsync(
        GetManifestEnsQuery query, CancellationToken cancellationToken) =>
        declarations.GetAsync(query.Id, cancellationToken);
}
