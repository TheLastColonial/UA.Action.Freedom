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
    int? ConvoyId,
    string? PurchaserName,
    DateTime? PurchaseDate,
    int WeightKg)
{
    public CreateVehicleCommand ToCommand() => new(
        Vin, Plate, Brand, Model, Colour, Transmission, Notes, Mileage, Servicing,
        Year, Fuel, ConvoyId, PurchaserName, PurchaseDate, WeightKg);
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
    int? ConvoyId,
    string? PurchaserName,
    DateTime? PurchaseDate,
    int WeightKg)
{
    public UpdateVehicleCommand ToCommand(string vin) => new(
        vin, Plate, Brand, Model, Colour, Transmission, Notes, Mileage, Servicing,
        Year, Fuel, ConvoyId, PurchaserName, PurchaseDate, WeightKg);
}
