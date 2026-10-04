using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Declarations;

/// <summary>
/// The ICS2 Entry Summary Declaration this vehicle's crossing was accepted under.
/// </summary>
/// <remarks>
/// A reference and who obtained it, and nothing about the load. An ENS describes a consignment in
/// detail, but Freedom never holds that description: it is composed for a filing sheet, read once,
/// and what comes back is the MRN alone. So this record has nowhere to put a receiver, an address or
/// a contact — the same structural redaction as <see cref="ManifestDocumentRequest"/> and
/// <see cref="EloEnvelopeRequest"/>, and a unit test pins it.
/// </remarks>
/// <param name="DeclarationId">The <c>dbo.Declaration</c> row this is the detail of.</param>
/// <param name="Mrn">The Movement Reference Number ICS2 issued, eighteen characters.</param>
/// <param name="AcceptedAt">When ICS2 accepted it — the authority's timestamp, not ours.</param>
/// <param name="FiledBy">
/// Who filed it. A name or portal login rather than a <c>PersonId</c>: the filer holds an account at the
/// EU Customs Trader Portal, which is not something Freedom administers.
/// </param>
/// <param name="FilingReference">The filer's own reference for the submission, if they kept one.</param>
public sealed record EnsDeclarationReadModel(
    int DeclarationId,
    string Mrn,
    DateTimeOffset AcceptedAt,
    string FiledBy,
    string? FilingReference);

/// <summary>
/// Where the detail of a recorded ENS lives, keyed by its declaration.
/// </summary>
/// <remarks>
/// Blob storage, as before, and still the seam an IT Service Provider's adapter would drop into
/// (ADR 0003). <see cref="SaveAsync"/> creates and never replaces, and says which happened rather than
/// throwing: the write-once discipline the declaration row gets from a conditional <c>UPDATE</c>,
/// carried into storage. A withdrawn declaration keeps its blob under its own id, which is how its MRN
/// stays as history — a replacement is a new declaration with a new id.
/// </remarks>
public interface IEnsDeclarationStore
{
    Task<EnsDeclarationReadModel?> GetAsync(int declarationId, CancellationToken cancellationToken);

    /// <summary>Records the detail. Returns <see langword="false"/>, replacing nothing, when one exists.</summary>
    Task<bool> SaveAsync(EnsDeclarationReadModel declaration, CancellationToken cancellationToken);
}

/// <summary>Record the MRN ICS2 issued for this vehicle's crossing.</summary>
public sealed record RecordEnsDeclarationCommand(
    int ConvoyId,
    string Vin,
    string Mrn,
    DateTimeOffset AcceptedAt,
    string FiledBy,
    string? FilingReference = null);

public enum RecordEnsOutcome
{
    Recorded,
    VehicleNotOnConvoy,
    AlreadyRecorded,
    MalformedMrn,
}

public sealed class RecordEnsDeclarationHandler(
    IDeclarationRepository declarations, IEnsDeclarationStore store)
    : ICommandHandler<RecordEnsDeclarationCommand, RecordEnsOutcome>
{
    public async Task<RecordEnsOutcome> HandleAsync(
        RecordEnsDeclarationCommand command, CancellationToken cancellationToken)
    {
        // The shape is checked first: a malformed MRN is the caller's mistake whatever the vehicle turns
        // out to be, and one recorded here becomes an envelope French customs refuses with FONC-ERR-004.
        if (!EnsMrn.IsWellFormed(command.Mrn))
        {
            return RecordEnsOutcome.MalformedMrn;
        }

        var recorded = await declarations.RecordReferenceAsync(
            command.ConvoyId, command.Vin, DeclarationKind.Ens, null, command.Mrn, cancellationToken);

        if (recorded == RecordReferenceResult.VehicleNotOnConvoy)
        {
            return RecordEnsOutcome.VehicleNotOnConvoy;
        }

        if (recorded == RecordReferenceResult.AlreadyRecorded)
        {
            return RecordEnsOutcome.AlreadyRecorded;
        }

        var declaration = await declarations.GetCurrentAsync(
            command.ConvoyId, command.Vin, DeclarationKind.Ens, null, cancellationToken);

        return declaration is not null
            && await store.SaveAsync(
                new EnsDeclarationReadModel(
                    declaration.Id, command.Mrn, command.AcceptedAt, command.FiledBy, command.FilingReference),
                cancellationToken)
            ? RecordEnsOutcome.Recorded
            : RecordEnsOutcome.AlreadyRecorded;
    }
}

/// <summary>Set this vehicle's ENS aside, because it was invalidated in ICS2 and will be refiled.</summary>
public sealed record SupersedeEnsDeclarationCommand(int ConvoyId, string Vin);

public enum SupersedeEnsOutcome
{
    Superseded,
    NotRecorded,
}

public sealed class SupersedeEnsDeclarationHandler(IDeclarationRepository declarations)
    : ICommandHandler<SupersedeEnsDeclarationCommand, SupersedeEnsOutcome>
{
    public async Task<SupersedeEnsOutcome> HandleAsync(
        SupersedeEnsDeclarationCommand command, CancellationToken cancellationToken) =>
        await declarations.WithdrawAsync(command.ConvoyId, command.Vin, DeclarationKind.Ens, null, cancellationToken)
            ? SupersedeEnsOutcome.Superseded
            : SupersedeEnsOutcome.NotRecorded;
}

/// <summary>Has this vehicle's crossing got an accepted ENS yet?</summary>
public sealed record GetEnsDeclarationQuery(int ConvoyId, string Vin);

public sealed class GetEnsDeclarationHandler(IDeclarationRepository declarations, IEnsDeclarationStore store)
    : IQueryHandler<GetEnsDeclarationQuery, EnsDeclarationReadModel?>
{
    public async Task<EnsDeclarationReadModel?> HandleAsync(
        GetEnsDeclarationQuery query, CancellationToken cancellationToken)
    {
        var declaration = await declarations.GetCurrentAsync(
            query.ConvoyId, query.Vin, DeclarationKind.Ens, null, cancellationToken);

        return declaration is null ? null : await store.GetAsync(declaration.Id, cancellationToken);
    }
}

/// <summary>What would be filed for this vehicle's ENS, or null when it has no manifest to read cargo from.</summary>
public sealed record GetVehicleEnsFilingSheetQuery(int ConvoyId, string Vin);

public sealed class GetVehicleEnsFilingSheetHandler(
    Manifests.IManifestRepository manifests,
    IQueryHandler<Manifests.GetEnsFilingSheetQuery, Manifests.EnsFilingSheetReadModel?> sheets)
    : IQueryHandler<GetVehicleEnsFilingSheetQuery, Manifests.EnsFilingSheetReadModel?>
{
    public async Task<Manifests.EnsFilingSheetReadModel?> HandleAsync(
        GetVehicleEnsFilingSheetQuery query, CancellationToken cancellationToken)
    {
        var manifest = await manifests.GetForVehicleAsync(query.ConvoyId, query.Vin, cancellationToken);

        return manifest is null
            ? null
            : await sheets.HandleAsync(new Manifests.GetEnsFilingSheetQuery(manifest.Id), cancellationToken);
    }
}
