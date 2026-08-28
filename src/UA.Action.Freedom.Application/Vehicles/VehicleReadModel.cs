using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Vehicles;

/// <summary>
/// A donated vehicle as this slice persists and returns it: the scalar columns plus a loose
/// <see cref="ConvoyId"/> and a denormalised <see cref="PurchaserName"/>. The domain's
/// <see cref="Vehicle"/> entity carries non-nullable <c>Convoy</c> / <c>Purchaser</c>
/// navigation and no identity, so it cannot be hydrated from a row yet — reconciling the two
/// is follow-up work. This record is the read model for queries and the write shape the
/// repository takes.
/// </summary>
public sealed record VehicleReadModel(
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
    int WeightKg);
