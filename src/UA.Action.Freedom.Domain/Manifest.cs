namespace UA.Action.Freedom.Domain;

/// <summary>
/// List of Boxes allocated to a <see cref="Vehicle"/> in a <see cref="Convoy"/>
/// </summary>
public class Manifest
{
    /// <summary>
    /// Unique reference
    /// </summary>
    public required ManifestId Id { get; init; }

    /// <summary>
    /// Where the manifest has reached in <see cref="ManifestTransitions"/>
    /// </summary>
    public ManifestStatus Status { get; init; } = ManifestStatus.Created;

    /// <summary>
    /// Vehicle allocated to transport the <see cref="Boxes"/>. Null until one is assigned.
    /// </summary>
    public Vehicle? Vehicle { get; init; }

    /// <summary>
    /// <see cref="Driver"/> allocated for UK to Europe Route. Null until the team is assigned.
    /// </summary>
    public DriverTeam? DriverUK { get; init; }

    /// <summary>
    /// <see cref="Driver"/> allocated for Europe to Ukraine Route. Null until the team is assigned.
    /// </summary>
    public DriverTeam? DriverBorder { get; init; }

    /// <summary>
    /// Cargo to be transported
    /// </summary>
    public Box[] Boxes { get; init; } = [];

    /// <summary>
    /// Text block for passing additional information or comments
    /// </summary>
    public string? DeliveryNotes { get; init; }

    /// <summary>
    /// Completed Ferry Booking
    /// </summary>
    public bool FerryBookingComplete { get; init; }

    /// <summary>
    /// When the Goods Movement Reference was submitted to HMRC, if it has been.
    /// </summary>
    /// <remarks>
    /// Once this is set the manifest is frozen: docs/recommendations.md §5.2 records the ruling
    /// that no edit may modify a manifest after its GMR is created, because the vehicle would then
    /// arrive at the border carrying something HMRC was not told about.
    /// </remarks>
    public DateTime? GmrSubmittedAt { get; init; }

    /// <summary>
    /// Total weight of <see cref="Vehicle"/>, Cargo etc for border checks in kilograms
    /// </summary>
    /// <returns>Total Kilograms</returns>
    public int TotalWeightKg() =>
        (this.Vehicle?.WeightKg ?? 0)
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
/// Status of a Manifest to manage flow, as drawn in docs/manifest-status.puml.
/// </summary>
public enum ManifestStatus
{
    /// <summary>Created but not populated completely.</summary>
    Created = 0,

    /// <summary>Approval has been requested.</summary>
    Proposed = 1,

    /// <summary>Approval was refused. Recoverable — it may be proposed again.</summary>
    Rejected = 2,

    /// <summary>Approved. Paperwork generation and box preparation follow.</summary>
    Confirmed = 3,

    /// <summary>Volunteers are preparing the boxes for transit.</summary>
    Preparing = 4,

    /// <summary>Ready for collection.</summary>
    Ready = 5,

    /// <summary>On a convoy.</summary>
    InTransit = 6,

    /// <summary>Arrived at the ultimate destination.</summary>
    Delivered = 7,

    /// <summary>Lost during transit.</summary>
    Lost = 8,

    /// <summary>Returned by the destination.</summary>
    Returned = 9,
}

/// <summary>
/// The edges of the manifest state machine.
/// </summary>
/// <remarks>
/// Kept as data rather than scattered <c>if</c>s in the handlers, so that the diagram and the code
/// can be read against each other. The happy path is linear and the only way backwards is
/// <see cref="ManifestStatus.Rejected"/> to <see cref="ManifestStatus.Proposed"/> — nothing may
/// reopen a manifest once it is confirmed, because confirmation is what releases it to GMR
/// submission (docs/recommendations.md §5.2).
/// </remarks>
public static class ManifestTransitions
{
    private static readonly HashSet<(ManifestStatus From, ManifestStatus To)> Allowed =
    [
        (ManifestStatus.Created, ManifestStatus.Proposed),
        (ManifestStatus.Created, ManifestStatus.Rejected),
        (ManifestStatus.Proposed, ManifestStatus.Rejected),
        (ManifestStatus.Rejected, ManifestStatus.Proposed),
        (ManifestStatus.Proposed, ManifestStatus.Confirmed),
        (ManifestStatus.Confirmed, ManifestStatus.Preparing),
        (ManifestStatus.Preparing, ManifestStatus.Ready),
        (ManifestStatus.Ready, ManifestStatus.InTransit),
        (ManifestStatus.InTransit, ManifestStatus.Delivered),
        (ManifestStatus.InTransit, ManifestStatus.Lost),
        (ManifestStatus.Delivered, ManifestStatus.Returned),
    ];

    /// <summary>
    /// Whether a manifest in <paramref name="from"/> may move to <paramref name="to"/>.
    /// </summary>
    public static bool CanTransition(ManifestStatus from, ManifestStatus to) => Allowed.Contains((from, to));
}
