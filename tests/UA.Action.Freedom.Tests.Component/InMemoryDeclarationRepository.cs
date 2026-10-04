using UA.Action.Freedom.Application.Declarations;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Stands in for <c>dbo.Declaration</c>. It enforces what the SQL does: the vehicle has to be on the
/// convoy's truck list, at most one declaration per scope and kind that is not withdrawn, a reference is
/// write-once (a refused declaration may be recorded afresh), only a filed declaration can be refused,
/// and withdrawing keeps the row and its reference as history.
/// </summary>
internal sealed class InMemoryDeclarationRepository(InMemoryConvoyRepository convoys) : IDeclarationRepository
{
    private readonly List<DeclarationReadModel> _rows = [];
    private int _nextId = 1;

    /// <summary>Seeds an accepted ENS, which an ELO needs before it can be recorded or filed.</summary>
    internal InMemoryDeclarationRepository WithAnAcceptedEns(int convoyId, string vin, string mrn = "25FR17551780961AT5")
    {
        _rows.Add(Row(convoyId, vin, DeclarationKind.Ens, null, DeclarationStatus.Accepted, mrn));
        return this;
    }

    internal IReadOnlyList<DeclarationReadModel> All => _rows;

    private DeclarationReadModel Row(
        int convoyId, string vin, DeclarationKind kind, Guid? receiverRef, DeclarationStatus status, string? reference) =>
        new(_nextId++, convoyId, vin, kind, status, receiverRef, reference, null, null, DateTime.UtcNow, null, DateTime.UtcNow);

    private int IndexOfCurrent(int convoyId, string vin, DeclarationKind kind, Guid? receiverRef) =>
        _rows.FindIndex(row =>
            row.ConvoyId == convoyId
            && string.Equals(row.Vin, vin, StringComparison.OrdinalIgnoreCase)
            && row.Kind == kind
            && row.ReceiverRef == receiverRef
            && row.Status != DeclarationStatus.Withdrawn);

    public Task<IReadOnlyList<DeclarationReadModel>> ListAsync(int convoyId, string vin, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DeclarationReadModel>>(
            _rows.Where(row => row.ConvoyId == convoyId
                               && string.Equals(row.Vin, vin, StringComparison.OrdinalIgnoreCase)).ToList());

    public Task<DeclarationReadModel?> GetCurrentAsync(
        int convoyId, string vin, DeclarationKind kind, Guid? receiverRef, CancellationToken cancellationToken)
    {
        var index = IndexOfCurrent(convoyId, vin, kind, receiverRef);
        return Task.FromResult(index < 0 ? null : _rows[index]);
    }

    public Task<RecordReferenceResult> RecordReferenceAsync(
        int convoyId, string vin, DeclarationKind kind, Guid? receiverRef, string? reference,
        CancellationToken cancellationToken)
    {
        if (!convoys.IsOnTruckList(convoyId, vin))
        {
            return Task.FromResult(RecordReferenceResult.VehicleNotOnConvoy);
        }

        var target = DeclarationTransitions.RecordedStatus(kind);
        var index = IndexOfCurrent(convoyId, vin, kind, receiverRef);

        if (index < 0)
        {
            _rows.Add(Row(convoyId, vin, kind, receiverRef, target, reference));
            return Task.FromResult(RecordReferenceResult.Recorded);
        }

        var existing = _rows[index];

        if (existing.Reference is not null && existing.Status != DeclarationStatus.Refused)
        {
            return Task.FromResult(RecordReferenceResult.AlreadyRecorded);
        }

        _rows[index] = existing with { Status = target, Reference = reference, ReasonCode = null, LastChangedAt = DateTime.UtcNow };
        return Task.FromResult(RecordReferenceResult.Recorded);
    }

    public Task<bool> RefuseAsync(int id, string reasonCode, CancellationToken cancellationToken)
    {
        var index = _rows.FindIndex(row => row.Id == id && row.Status == DeclarationStatus.Filed);

        if (index < 0)
        {
            return Task.FromResult(false);
        }

        _rows[index] = _rows[index] with { Status = DeclarationStatus.Refused, ReasonCode = reasonCode };
        return Task.FromResult(true);
    }

    public Task<bool> WithdrawAsync(
        int convoyId, string vin, DeclarationKind kind, Guid? receiverRef, CancellationToken cancellationToken)
    {
        var index = IndexOfCurrent(convoyId, vin, kind, receiverRef);

        if (index < 0 || _rows[index].Status is not (DeclarationStatus.Filed or DeclarationStatus.Accepted))
        {
            return Task.FromResult(false);
        }

        _rows[index] = _rows[index] with { Status = DeclarationStatus.Withdrawn };
        return Task.FromResult(true);
    }
}
