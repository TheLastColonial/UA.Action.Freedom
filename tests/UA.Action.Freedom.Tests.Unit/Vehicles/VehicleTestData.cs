using UA.Action.Freedom.Application.Vehicles;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Vehicles;

/// <summary>
/// Factory for vehicle test data. Every field has a sensible default; a test overrides only
/// what it is actually about.
/// </summary>
internal static class VehicleTestData
{
    internal static CreateVehicleCommand ACreateCommand(
        string vin = "WVWZZZ1JZXW000001",
        string plate = "AB12CDE",
        int? convoyId = null,
        string? purchaserName = "operator",
        decimal? maxCargoWeightKg = 800.50m,
        decimal? cargoWidthCm = 150.25m,
        decimal? cargoDepthCm = 300.00m,
        decimal? cargoHeightCm = 180.75m) => new(
        Vin: vin,
        Plate: plate,
        Brand: "Volkswagen",
        Model: "Transporter",
        Colour: "White",
        Transmission: TransmissionType.Manual,
        Notes: null,
        Mileage: 92_000,
        Servicing: false,
        Year: 2016,
        Fuel: FuelType.Diesel,
        ConvoyId: convoyId,
        PurchaserName: purchaserName,
        PurchaseDate: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        WeightKg: 1_400,
        MaxCargoWeightKg: maxCargoWeightKg,
        CargoWidthCm: cargoWidthCm,
        CargoDepthCm: cargoDepthCm,
        CargoHeightCm: cargoHeightCm);

    internal static UpdateVehicleCommand AnUpdateCommand(
        string vin = "WVWZZZ1JZXW000001",
        string plate = "ZZ99ZZZ",
        int weightKg = 1_500,
        decimal? maxCargoWeightKg = 800.50m,
        decimal? cargoWidthCm = 150.25m,
        decimal? cargoDepthCm = 300.00m,
        decimal? cargoHeightCm = 180.75m) => new(
        Vin: vin,
        Plate: plate,
        Brand: "Volkswagen",
        Model: "Transporter",
        Colour: "Blue",
        Transmission: TransmissionType.Manual,
        Notes: "Repainted",
        Mileage: 95_000,
        Servicing: true,
        Year: 2016,
        Fuel: FuelType.Diesel,
        ConvoyId: null,
        PurchaserName: "operator",
        PurchaseDate: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        WeightKg: weightKg,
        MaxCargoWeightKg: maxCargoWeightKg,
        CargoWidthCm: cargoWidthCm,
        CargoDepthCm: cargoDepthCm,
        CargoHeightCm: cargoHeightCm);

    internal static VehicleReadModel AReadModel(string vin = "WVWZZZ1JZXW000001") => new(
        Vin: vin,
        Plate: "AB12CDE",
        Brand: "Volkswagen",
        Model: "Transporter",
        Colour: "White",
        Transmission: TransmissionType.Manual,
        Notes: null,
        Mileage: 92_000,
        Servicing: false,
        Year: 2016,
        Fuel: FuelType.Diesel,
        ConvoyId: null,
        PurchaserName: "operator",
        PurchaseDate: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        WeightKg: 1_400,
        MaxCargoWeightKg: 800.50m,
        CargoWidthCm: 150.25m,
        CargoDepthCm: 300.00m,
        CargoHeightCm: 180.75m);
}
