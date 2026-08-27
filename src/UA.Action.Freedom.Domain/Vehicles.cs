namespace UA.Action.Freedom.Domain;

/// <summary>
/// Truck or car that is being donated
/// </summary>
public class Vehicle
{
    /// <summary>
    /// Vehicle Identification Number
    /// </summary>
    public string VIN { get; init; }

    /// <summary>
    /// Licence Plate Number
    /// </summary>
    public string Plate { get; init; }

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
    /// Year of manufacture
    /// </summary>
    public int Year { get; init; }

    /// <summary>
    /// Type of fuel to use
    /// </summary>
    public FuelType Fuel { get; init; }

    /// <summary>
    /// Grouping of vehicles
    /// </summary>
    public Convoy Convoy { get; init; }

    /// <summary>
    /// Individual responsible for the purchase order of a vehicle
    /// </summary>
    public Person Purchaser { get; init; }

    /// <summary>
    /// Timestamp of the purchase
    /// </summary>
    public DateTime PurchaseDate { get; init; }

    /// <summary>
    /// History of drivers
    /// </summary>
    public List<Driver>? Drivers { get; init; }

    /// <summary>
    /// Kerb Weight in Kilograms
    /// </summary>
    public int WeightKg { get; init; }
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