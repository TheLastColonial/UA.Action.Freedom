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
        string? purchaserName = "operator") => new(
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
        WeightKg: 1_400);

    internal static UpdateVehicleCommand AnUpdateCommand(
        string vin = "WVWZZZ1JZXW000001",
        string plate = "ZZ99ZZZ",
        int weightKg = 1_500) => new(
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
        WeightKg: weightKg);

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
        WeightKg: 1_400);
}
