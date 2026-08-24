namespace UA.Action.Freedom.Domain;

/// <summary>
/// List of Boxes allocated to a <see cref="Veichle"/> in a <see cref="Convoy"/>
/// </summary>
public class Manifest
{
    /// <summary>
    /// Unique reference
    /// </summary>
    public ManifestId Id { get; init; }

    /// <summary>
    /// Veichle allocated to transport <see cref="Box[]"/>
    /// </summary>
    public Veichle Veichle { get; init; }

    /// <summary>
    /// <see cref="Driver"/> allocated for UK to Europe Route
    /// </summary>
    public DriverTeam DriverUK { get; init; }

    /// <summary>
    /// <see cref="Driver"/> allocated for Europe to Ukraine Rotue
    /// </summary>
    public DriverTeam DriverBorder { get; init; }

    /// <summary>
    /// Cargo to be transported
    /// </summary>
    public Box[] Boxes { get; init; }

    /// <summary>
    /// Text block for passing addtional informaiton or comments
    /// </summary>
    public string? DeliveryNotes { get; init; }

    /// <summary>
    /// Completed Ferry Booking
    /// </summary>
    public bool FerryBookingComplete { get; init; }

    /// <summary>
    /// Total weight of <see cref="Veichle"/>, Cargo etc for border checks in kilograms
    /// </summary>
    /// <returns>Total Kilograms</returns>
    public int TotalWeightKg() =>
        this.Veichle.WeightKg
        + this.Boxes.Sum(box => box.WeightKg) // Cargo
        + 100 * 2 // 2x Driver + Bags
        + 45; // Fuel
}

/// <summary>
/// Unique reference to a combined cargo, convoy and drivers
/// </summary>
/// <param name="Value"></param>
public record ManifestId(string Value);

/// <summary>
/// Status of a Manifest to manage flow
/// </summary>
public record ManifestStatus
{
    public int Id { get; set; }
    public string Name { get; set; }

    protected ManifestStatus(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public ManifestStatus Proposed => new ManifestStatus(1, "Proposed");
    public ManifestStatus Confirmed => new ManifestStatus(2, "Confirmed");
    public ManifestStatus Prepared => new ManifestStatus(3, "Prepared");
    public ManifestStatus Transit => new ManifestStatus(4, "Transit");
    public ManifestStatus Arrived => new ManifestStatus(5, "Arrived");
    public ManifestStatus Lost => new ManifestStatus(6, "Lost");
}
