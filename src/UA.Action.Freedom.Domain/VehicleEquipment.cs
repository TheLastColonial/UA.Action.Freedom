namespace UA.Action.Freedom.Domain;

/// <summary>
/// Equipment the charity buys for a vehicle, such as warning triangles (O13). It is accounted for separately from
/// donations: it has no donor and is never part of the value delivered. What it costs counts against the budget's
/// Other line.
/// </summary>
public static class VehicleEquipment
{
    /// <summary>The cost recorded for the line, else the quantity at the catalogue's unit cost, else nothing.</summary>
    public static decimal CountedCostGbp(int quantity, decimal? costGbp, decimal? unitCostGbp) =>
        costGbp ?? quantity * (unitCostGbp ?? 0m);
}
