using UA.Action.Freedom.Application.Vehicles;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Api.Vehicles;

/// <summary>
/// Body of <c>POST /vehicles</c>. VIN is the identifier and is supplied here; on
/// <c>PUT</c> it comes from the route instead.
/// </summary>
public sealed record CreateVehicleRequest(
    string Vin,
    string Plate,
    string? Brand,
    string? Model,
    string? Colour,
    TransmissionType Transmission,
    string? Notes,
    int? Mileage,
    bool Servicing,
    int Year,
    FuelType Fuel,
    string? PurchaserName,
    DateTime? PurchaseDate,
    int WeightKg,
    decimal? MaxCargoWeightKg = null,
    decimal? CargoWidthCm = null,
    decimal? CargoDepthCm = null,
    decimal? CargoHeightCm = null)
{
    public CreateVehicleCommand ToCommand() => new(
        Vin, Plate, Brand, Model, Colour, Transmission, Notes, Mileage, Servicing,
        Year, Fuel, PurchaserName, PurchaseDate, WeightKg,
        MaxCargoWeightKg, CargoWidthCm, CargoDepthCm, CargoHeightCm);
}

/// <summary>Body of <c>PUT /vehicles/{vin}/inspection</c>. The route supplies the VIN.</summary>
public sealed record RecordInspectionRequest(InspectionStatus Status, string? Notes)
{
    public RecordInspectionCommand ToCommand(string vin) => new(vin, Status, Notes);
}

/// <summary>Body of <c>PUT /vehicles/{vin}</c>. The route supplies the VIN.</summary>
public sealed record UpdateVehicleRequest(
    string Plate,
    string? Brand,
    string? Model,
    string? Colour,
    TransmissionType Transmission,
    string? Notes,
    int? Mileage,
    bool Servicing,
    int Year,
    FuelType Fuel,
    string? PurchaserName,
    DateTime? PurchaseDate,
    int WeightKg,
    decimal? MaxCargoWeightKg = null,
    decimal? CargoWidthCm = null,
    decimal? CargoDepthCm = null,
    decimal? CargoHeightCm = null)
{
    public UpdateVehicleCommand ToCommand(string vin) => new(
        vin, Plate, Brand, Model, Colour, Transmission, Notes, Mileage, Servicing,
        Year, Fuel, PurchaserName, PurchaseDate, WeightKg,
        MaxCargoWeightKg, CargoWidthCm, CargoDepthCm, CargoHeightCm);
}
