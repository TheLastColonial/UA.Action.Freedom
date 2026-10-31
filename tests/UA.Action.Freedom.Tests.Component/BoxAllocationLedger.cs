using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Standing in for <c>dbo.ConvoyVehicleBoxAllocation</c> and the <c>dbo.Box</c> rows it points at.
/// </summary>
/// <remarks>
/// The convoy fake writes allocations and the manifest fake reads them for its document and filing
/// sheet, exactly as the two SQL repositories share one table, so the two fakes share one ledger.
/// The rule that a box is on at most one entry is <see cref="BoxAllocations"/>, the same code the
/// domain tests pin.
/// </remarks>
internal sealed class BoxAllocationLedger
{
    private readonly Dictionary<int, ManifestBoxReadModel> boxes = [];
    private readonly HashSet<int> voided = [];
    private IReadOnlyList<BoxAllocation> allocations = [];
    private readonly List<BoxOutcomeReadModel> outcomes = [];

    public IReadOnlyList<BoxAllocation> Allocations => allocations;

    /// <summary>
    /// What the SQL does in the same transaction as a change to a vehicle's load: send its approved manifest back to
    /// Proposed (X3). Set by the manifest fake, which holds the manifests.
    /// </summary>
    internal Action<int, string>? ReopenManifest { get; set; }

    public void Reopen(int convoyId, string vin) => ReopenManifest?.Invoke(convoyId, vin);

    public void KnowBox(ManifestBoxReadModel box) => boxes[box.BoxId] = box;

    /// <summary>Takes in everything another ledger holds, so two fakes seeded apart end up sharing one.</summary>
    public void Absorb(BoxAllocationLedger other)
    {
        foreach (var box in other.boxes.Values)
        {
            boxes[box.BoxId] = box;
        }

        allocations = [.. allocations, .. other.allocations.Where(a => allocations.All(x => x.BoxId != a.BoxId))];
    }

    /// <summary>
    /// A box replaced by another: its cargo allocation goes to the replacement and the voided box is no longer
    /// known as cargo, as the SQL moves the allocation row and the voided box carries none.
    /// </summary>
    public void Replace(int voidedBoxId, ManifestBoxReadModel replacement)
    {
        var carried = AllocationOf(voidedBoxId);
        boxes.Remove(voidedBoxId);
        voided.Add(voidedBoxId);
        boxes[replacement.BoxId] = replacement;
        allocations = [.. allocations.Select(a => a.BoxId == voidedBoxId ? a with { BoxId = replacement.BoxId } : a)];

        if (carried is not null)
        {
            Reopen(carried.ConvoyId.Value, carried.Vin);
        }
    }

    public bool Knows(int boxId) => boxes.ContainsKey(boxId);

    public bool IsVoided(int boxId) => voided.Contains(boxId);

    public BoxAllocation? AllocationOf(int boxId) => allocations.FirstOrDefault(a => a.BoxId == boxId);

    public AllocateOutcome Allocate(ConvoyVehicle entry, int boxId)
    {
        var result = BoxAllocations.Allocate(allocations, entry, boxId, DateTime.UtcNow);
        allocations = result.Allocations;
        return result.Outcome;
    }

    public bool Remove(ConvoyVehicle entry, int boxId)
    {
        var result = BoxAllocations.Remove(allocations, entry, boxId);
        allocations = result.Allocations;
        return result.Outcome == RemoveAllocationOutcome.Removed;
    }

    /// <summary>Clears an entry's cargo, as the cascade from the truck-list row does.</summary>
    public void ForgetEntry(int convoyId, string vin) =>
        allocations = [.. allocations.Where(a => !(a.ConvoyId.Value == convoyId
            && string.Equals(a.Vin, vin, StringComparison.OrdinalIgnoreCase)))];

    /// <summary>The boxes allocated to an entry that have no outcome yet on this convoy, as the delivery INSERT selects them.</summary>
    public IReadOnlyList<int> BoxesWithoutOutcome(int convoyId, string vin) =>
        [.. allocations
            .Where(a => a.ConvoyId.Value == convoyId && string.Equals(a.Vin, vin, StringComparison.OrdinalIgnoreCase)
                        && !IsVoided(a.BoxId) && CurrentOutcome(convoyId, a.BoxId) is null)
            .Select(a => a.BoxId)];

    public BoxOutcomeReadModel? CurrentOutcome(int convoyId, int boxId) =>
        outcomes.LastOrDefault(o => o.ConvoyId == convoyId && o.BoxId == boxId);

    public IReadOnlyList<BoxOutcomeReadModel> CurrentOutcomes(int convoyId) =>
        [.. outcomes.Where(o => o.ConvoyId == convoyId).GroupBy(o => o.BoxId).Select(g => g.Last()).OrderBy(o => o.BoxId)];

    /// <summary>
    /// Mirrors <c>BoxOutcomeRepository.RecordAsync</c>: the write conflicts unless the outcome the caller saw is still
    /// the current one, and a return to a hub takes the box off its vehicle.
    /// </summary>
    public RecordBoxOutcomeResult RecordOutcome(RecordBoxOutcome write, DateTime now)
    {
        if (CurrentOutcome(write.ConvoyId, write.BoxId)?.Outcome != write.From)
        {
            return RecordBoxOutcomeResult.Conflict;
        }

        outcomes.Add(new BoxOutcomeReadModel(
            write.BoxId, write.ConvoyId, write.Vin, write.Outcome, write.Reason, write.HubLocationId, now));

        if (write.Outcome == BoxOutcome.ReturnedToHub)
        {
            allocations = [.. allocations.Where(a => !(a.BoxId == write.BoxId && a.ConvoyId.Value == write.ConvoyId))];
        }

        if (write.Outcome is BoxOutcome.Seized or BoxOutcome.Undeliverable or BoxOutcome.ReturnedToHub)
        {
            Reopen(write.ConvoyId, write.Vin);
        }

        return RecordBoxOutcomeResult.Recorded;
    }

    private bool LeftTheLoad(int convoyId, int boxId) =>
        CurrentOutcome(convoyId, boxId)?.Outcome is BoxOutcome.Seized or BoxOutcome.Undeliverable;

    public IReadOnlyList<ManifestBoxReadModel> BoxesOn(int convoyId, string vin) =>
        allocations
            .Where(a => a.ConvoyId.Value == convoyId && string.Equals(a.Vin, vin, StringComparison.OrdinalIgnoreCase)
                        && !LeftTheLoad(convoyId, a.BoxId))
            .OrderBy(a => a.BoxId)
            .Select(a => boxes[a.BoxId])
            .ToList();
}
