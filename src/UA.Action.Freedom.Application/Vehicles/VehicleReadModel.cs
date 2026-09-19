using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Vehicles;

/// <summary>
/// A donated vehicle as this slice persists and returns it: the scalar columns plus a loose
/// <see cref="ConvoyId"/> and a denormalised <see cref="PurchaserName"/>. The domain's
/// <see cref="Vehicle"/> entity carries non-nullable <c>Convoy</c> / <c>Purchaser</c>
/// navigation and no identity, so it cannot be hydrated from a row yet — reconciling the two
/// is follow-up work. This record is the read model for queries and the write shape the
/// repository takes. The inspection pair is read-only through this shape: the repository never
/// writes it from here, only through <see cref="IVehicleRepository.RecordInspectionAsync"/>, so
/// an ordinary vehicle edit cannot set, clear or forge a Mechanic's result.
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
    int WeightKg,
    decimal? MaxCargoWeightKg,
    decimal? CargoWidthCm,
    decimal? CargoDepthCm,
    decimal? CargoHeightCm,
    InspectionStatus InspectionStatus = InspectionStatus.Pending,
    string? InspectionNotes = null);
