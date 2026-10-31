using UA.Action.Freedom.Application.Boxes;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Standing in for <c>dbo.BoxOutcome</c>. The outcomes live in the allocation ledger the convoy and manifest fakes share,
/// because the SQL reads them in the same queries that read the allocations; a return to a hub also puts the box at the hub.
/// </summary>
internal sealed class InMemoryBoxOutcomeRepository(InMemoryConvoyRepository convoys, InMemoryBoxRepository? boxes)
    : IBoxOutcomeRepository
{
    private BoxAllocationLedger ledger => convoys.Ledger;

    public Task<BoxOutcomeReadModel?> GetCurrentAsync(int convoyId, int boxId, CancellationToken cancellationToken) =>
        Task.FromResult(ledger.CurrentOutcome(convoyId, boxId));

    public Task<IReadOnlyList<BoxOutcomeReadModel>> ListCurrentAsync(int convoyId, CancellationToken cancellationToken) =>
        Task.FromResult(ledger.CurrentOutcomes(convoyId));

    public Task<int?> DeliverVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        if (!convoys.StampDelivered(convoyId, vin))
        {
            return Task.FromResult<int?>(null);
        }

        var undecided = ledger.BoxesWithoutOutcome(convoyId, vin);

        foreach (var boxId in undecided)
        {
            ledger.RecordOutcome(
                new RecordBoxOutcome(convoyId, vin, boxId, null, Domain.BoxOutcome.Delivered, null, null), DateTime.UtcNow);
        }

        return Task.FromResult<int?>(undecided.Count);
    }

    public Task<int> AcceptGoodsListAsync(int convoyId, string vin, Guid receiverRef, CancellationToken cancellationToken)
    {
        // Mirrors the SQL: only this Receiver's delivered, un-voided boxes on this vehicle.
        var delivered = ledger.CurrentOutcomes(convoyId)
            .Where(o => o.Outcome == Domain.BoxOutcome.Delivered
                        && string.Equals(o.Vin, vin, StringComparison.OrdinalIgnoreCase)
                        && boxes?.Box(o.BoxId) is { ReceiverRef: { } receiver, Voided: false }
                        && receiver == receiverRef)
            .ToList();

        foreach (var outcome in delivered)
        {
            ledger.RecordOutcome(
                new RecordBoxOutcome(convoyId, outcome.Vin, outcome.BoxId, Domain.BoxOutcome.Delivered,
                    Domain.BoxOutcome.Accepted, null, null),
                DateTime.UtcNow);
        }

        return Task.FromResult(delivered.Count);
    }

    public Task<RecordBoxOutcomeResult> RecordAsync(RecordBoxOutcome outcome, CancellationToken cancellationToken)
    {
        var result = ledger.RecordOutcome(outcome, DateTime.UtcNow);

        if (result == RecordBoxOutcomeResult.Recorded
            && outcome.Outcome == Domain.BoxOutcome.ReturnedToHub
            && outcome.HubLocationId is { } hub)
        {
            boxes?.MoveTo(outcome.BoxId, hub);
        }

        return Task.FromResult(result);
    }
}
