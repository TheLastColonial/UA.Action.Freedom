namespace UA.Action.Freedom.Domain;

/// <summary>
/// Truck or car that is being donated
/// </summary>
public class Vehicle
{
    /// <summary>
    /// Vehicle Identification Number
    /// </summary>
    public required string VIN { get; init; }

    /// <summary>
    /// Licence Plate Number
    /// </summary>
    public required string Plate { get; init; }

    /// <summary>
    /// e.g. Ford
    /// </summary>
    public string? Brand { get; init; }

    /// <summary>
    /// e.g. Focus
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// e.g. Red
    /// </summary>
    public string? Colour { get; init; }

    /// <summary>
    /// Type of Transmission
    /// </summary>
    public TransmissionType Transmission { get; init; }

    /// <summary>
    /// Commentary of the vehicle for damages or other issues
    /// </summary>
    public string? Notes { get; init; }

    /// <summary>
    /// Mileage of the vehicle as used
    /// </summary>
    public int? Mileage { get; init; }
    public bool Servicing { get; init; }

    /// <summary>
    /// Current inspection status: Pending, Inspecting, Passed, or Failed
    /// </summary>
    public InspectionStatus InspectionStatus { get; init; } = InspectionStatus.Pending;

    /// <summary>
    /// Notes from the mechanic about defects or issues found during inspection
    /// </summary>
    public string? InspectionNotes { get; init; }

    /// <summary>
    /// Year of manufacture
    /// </summary>
    public int Year { get; init; }

    /// <summary>
    /// Type of fuel to use
    /// </summary>
    public FuelType Fuel { get; init; }

    /// <summary>
    /// When the vehicle was handed over in Ukraine, if it has been. A vehicle is itself part of
    /// the aid, so once it is handed over it is never offered for a convoy again.
    /// </summary>
    /// <remarks>
    /// Stamped by <c>POST /convoys/{id}/arrive</c> for vehicles whose manifest ended Delivered or
    /// Lost. Which convoy a vehicle is travelling with is <em>not</em> a field here — that is the
    /// truck list, <see cref="ConvoyVehicle"/>, which keeps its history instead of being nulled.
    /// </remarks>
    public DateTime? HandedOverAt { get; init; }

    /// <summary>
    /// Individual responsible for the purchase order of a vehicle. Null for a direct donation.
    /// </summary>
    public Person? Purchaser { get; init; }

    /// <summary>
    /// Timestamp of the purchase. Null for a direct donation.
    /// </summary>
    public DateTime? PurchaseDate { get; init; }

    /// <summary>
    /// Kerb Weight in Kilograms
    /// </summary>
    public int WeightKg { get; init; }

    /// <summary>
    /// Maximum cargo weight the vehicle can carry, in kilograms. Distinct from
    /// <see cref="WeightKg"/>, which is the vehicle's own kerb weight. Null until measured.
    /// </summary>
    public decimal? MaxCargoWeightKg { get; init; }

    /// <summary>Width of the cargo space, in centimetres. Null until measured.</summary>
    public decimal? CargoWidthCm { get; init; }

    /// <summary>Depth of the cargo space, in centimetres. Null until measured.</summary>
    public decimal? CargoDepthCm { get; init; }

    /// <summary>Height of the cargo space, in centimetres. Null until measured.</summary>
    public decimal? CargoHeightCm { get; init; }
}

/// <summary>
/// Type of transmission of a vehicle
/// </summary>
public enum TransmissionType
{
    Unknown = 0,
    Manual,
    Automatic
}

/// <summary>
/// Fuel used in the vehicle
/// </summary>
public enum FuelType
{
    Unknown = 0,
    Petrol,
    Diesel,
    Electric,
    Hybrid
}

/// <summary>
/// Status of vehicle inspection during servicing
/// </summary>
public enum InspectionStatus
{
    /// <summary>Vehicle has not been inspected yet</summary>
    Pending = 0,

    /// <summary>Vehicle is currently being inspected by the mechanic</summary>
    Inspecting = 1,

    /// <summary>Vehicle has passed inspection and is ready for convoy</summary>
    Passed = 2,

    /// <summary>Vehicle has failed inspection and is not ready for convoy</summary>
    Failed = 3
}