namespace UA.Action.Freedom.Application.Convoys;

/// <summary>An entry in the catalogue of equipment the charity buys. The unit cost is a default, not a promise.</summary>
public sealed record EquipmentItemReadModel(
    int Id,
    string Name,
    decimal? UnitCostGbp,
    string? LastChangedByName = null,
    DateTime? LastChangedAt = null);

/// <summary>One line of equipment on a vehicle: what is written when the vehicle's equipment is replaced.</summary>
public sealed record VehicleEquipmentLine(int EquipmentItemId, int Quantity, decimal? CostGbp);

/// <summary>
/// A line as read. <see cref="CountedCostGbp"/> is what it adds to the Other line of the budget: the recorded cost,
/// else the quantity at the catalogue's unit cost.
/// </summary>
public sealed record VehicleEquipmentReadModel(
    string Vin,
    int EquipmentItemId,
    string Name,
    int Quantity,
    decimal? UnitCostGbp,
    decimal? CostGbp,
    string? LastChangedByName = null,
    DateTime? LastChangedAt = null)
{
    public decimal CountedCostGbp => Domain.VehicleEquipment.CountedCostGbp(Quantity, CostGbp, UnitCostGbp);
}

public enum ReplaceEquipmentResult
{
    Replaced,
    VehicleNotOnConvoy,
    UnknownItem
}

/// <summary>
/// Persistence port for the equipment catalogue and the equipment on a vehicle (O13). Kept apart from the budget: it
/// is accounted for separately, has no donor and is not part of the value delivered.
/// </summary>
public interface IVehicleEquipmentRepository
{
    Task<IReadOnlyList<EquipmentItemReadModel>> ListItemsAsync(CancellationToken cancellationToken);

    /// <summary>Adds a catalogue entry and returns its identifier, or null when the name is already in the catalogue.</summary>
    Task<int?> AddItemAsync(string name, decimal? unitCostGbp, CancellationToken cancellationToken);

    Task<IReadOnlyList<VehicleEquipmentReadModel>> ListForVehicleAsync(
        int convoyId, string vin, CancellationToken cancellationToken);

    Task<IReadOnlyList<VehicleEquipmentReadModel>> ListForConvoyAsync(int convoyId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces everything on the vehicle in one transaction. Nothing changes when the vehicle is not on the convoy
    /// or a line names an item that is not in the catalogue.
    /// </summary>
    Task<ReplaceEquipmentResult> ReplaceForVehicleAsync(
        int convoyId, string vin, IReadOnlyList<VehicleEquipmentLine> lines, CancellationToken cancellationToken);
}
