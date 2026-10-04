namespace UA.Action.Freedom.Domain;

/// <summary>One item in a box as a declaration sees it: what customs asks about, and nothing else.</summary>
public sealed record LoadItem(Guid ItemId, int CategoryId, int? Quantity, decimal? ValueGbp, string? CommodityCode);

/// <summary>A box on the vehicle as a declaration sees it.</summary>
/// <param name="ReceiverRegistered">Whether the box's Receiver was registered when the load was read.</param>
public sealed record LoadBox(
    int BoxId, int WeightKg, Guid? ReceiverRef, bool ReceiverRegistered, IReadOnlyList<LoadItem> Items);

/// <summary>
/// What a vehicle was carrying when a declaration was written from it (ADR 0005). Stored when the
/// declaration becomes ready to file and compared with the load as it is now, so staleness is derived
/// and nobody has to remember to flag it. Holds only opaque identifiers and customs-relevant figures:
/// no address, contact or name.
/// </summary>
/// <param name="Version">
/// The fields this snapshot was written with. A later version adds fields; an older snapshot is
/// compared on the fields it has, so adding one does not make every filed declaration stale.
/// </param>
public sealed record LoadSnapshot(int Version, string Vin, bool VehicleWithdrawn, IReadOnlyList<LoadBox> Boxes)
{
    public const int CurrentVersion = 1;
}

/// <summary>The rules of docs/domain/customs-declarations.md § Staleness as a pure function.</summary>
public static class Staleness
{
    /// <summary>
    /// True when <paramref name="current"/> differs from <paramref name="snapshot"/> in anything a
    /// declaration depends on. Moving a box between bays, reissuing its label, recording delivery
    /// progress and changing the crew are not in either type, so they cannot make anything stale.
    /// </summary>
    public static bool IsStale(LoadSnapshot snapshot, LoadSnapshot current) =>
        Fingerprint(snapshot, snapshot.Version) != Fingerprint(current, snapshot.Version)
        || ReceiverStoppedBeingRegistered(snapshot, current);

    // A box that is gone is already a difference above; here only the receivers of boxes still present.
    private static bool ReceiverStoppedBeingRegistered(LoadSnapshot snapshot, LoadSnapshot current) =>
        snapshot.Boxes
            .Where(box => box.ReceiverRegistered)
            .Any(box => current.Boxes.Any(now => now.BoxId == box.BoxId && !now.ReceiverRegistered));

    /// <summary>
    /// A canonical rendering of the fields <paramref name="version"/> knows about. Every version so far
    /// has the same fields; a later one adds to the branch for its own number and leaves this one alone.
    /// </summary>
    private static string Fingerprint(LoadSnapshot load, int version)
    {
        var boxes = load.Boxes
            .OrderBy(box => box.BoxId)
            .Select(box =>
                $"{box.BoxId}:{box.WeightKg}:{box.ReceiverRef}:"
                + string.Join(
                    ",",
                    box.Items
                        .OrderBy(item => item.ItemId)
                        .Select(item => $"{item.ItemId}/{item.CategoryId}/{item.Quantity}/{item.ValueGbp}/{item.CommodityCode}")));

        return $"{load.Vin}|{load.VehicleWithdrawn}|{string.Join(";", boxes)}";
    }
}
