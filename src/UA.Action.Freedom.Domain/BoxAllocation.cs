namespace UA.Action.Freedom.Domain;

/// <summary>A box put on one truck-list entry — that vehicle's cargo.</summary>
public sealed record BoxAllocation(ConvoyId ConvoyId, string Vin, int BoxId, DateTime AllocatedAt);

public enum AllocateOutcome
{
    Allocated,
    Moved,
    AlreadyAllocated,
    VehicleWithdrawn,
}

public enum RemoveAllocationOutcome
{
    Removed,
    NotAllocated,
}

public sealed record AllocateResult(AllocateOutcome Outcome, IReadOnlyList<BoxAllocation> Allocations);

public sealed record RemoveAllocationResult(RemoveAllocationOutcome Outcome, IReadOnlyList<BoxAllocation> Allocations);

/// <summary>
/// The rule that a box is on at most one truck-list entry. The primary key on <c>BoxId</c> enforces
/// it in storage; this is the same rule where it can be read and tested.
/// </summary>
public static class BoxAllocations
{
    public static AllocateResult Allocate(
        IReadOnlyList<BoxAllocation> current, ConvoyVehicle entry, int boxId, DateTime now)
    {
        if (entry.Withdrawn)
        {
            return new AllocateResult(AllocateOutcome.VehicleWithdrawn, current);
        }

        var existing = current.FirstOrDefault(a => a.BoxId == boxId);
        if (existing is not null && existing.ConvoyId == entry.ConvoyId && existing.Vin == entry.Vin)
        {
            return new AllocateResult(AllocateOutcome.AlreadyAllocated, current);
        }

        var others = current.Where(a => a.BoxId != boxId);
        var allocation = new BoxAllocation(entry.ConvoyId, entry.Vin, boxId, now);

        return new AllocateResult(
            existing is null ? AllocateOutcome.Allocated : AllocateOutcome.Moved,
            [.. others, allocation]);
    }

    public static RemoveAllocationResult Remove(
        IReadOnlyList<BoxAllocation> current, ConvoyVehicle entry, int boxId)
    {
        var onThisEntry = current.Any(a => a.BoxId == boxId && a.ConvoyId == entry.ConvoyId && a.Vin == entry.Vin);

        return onThisEntry
            ? new RemoveAllocationResult(RemoveAllocationOutcome.Removed, [.. current.Where(a => a.BoxId != boxId)])
            : new RemoveAllocationResult(RemoveAllocationOutcome.NotAllocated, current);
    }
}
