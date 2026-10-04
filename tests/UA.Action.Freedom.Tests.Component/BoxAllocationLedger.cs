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

    public IReadOnlyList<BoxAllocation> Allocations => allocations;

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
        boxes.Remove(voidedBoxId);
        voided.Add(voidedBoxId);
        boxes[replacement.BoxId] = replacement;
        allocations = [.. allocations.Select(a => a.BoxId == voidedBoxId ? a with { BoxId = replacement.BoxId } : a)];
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

    public IReadOnlyList<ManifestBoxReadModel> BoxesOn(int convoyId, string vin) =>
        allocations
            .Where(a => a.ConvoyId.Value == convoyId && string.Equals(a.Vin, vin, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.BoxId)
            .Select(a => boxes[a.BoxId])
            .ToList();
}
