using UA.Action.Freedom.Application.Boxes;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Standing in for <c>dbo.BoxOutcome</c>. The outcomes live in the allocation ledger the convoy and manifest fakes share,
/// because the SQL reads them in the same queries that read the allocations; a return to a hub also puts the box at the hub.
/// </summary>
internal sealed class InMemoryBoxOutcomeRepository(BoxAllocationLedger ledger, InMemoryBoxRepository? boxes)
    : IBoxOutcomeRepository
{
    public Task<BoxOutcomeReadModel?> GetCurrentAsync(int convoyId, int boxId, CancellationToken cancellationToken) =>
        Task.FromResult(ledger.CurrentOutcome(convoyId, boxId));

    public Task<IReadOnlyList<BoxOutcomeReadModel>> ListCurrentAsync(int convoyId, CancellationToken cancellationToken) =>
        Task.FromResult(ledger.CurrentOutcomes(convoyId));

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
