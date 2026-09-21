namespace UA.Action.Freedom.Domain;

/// <summary>
/// The document pack for one <see cref="Vehicle"/> on one <see cref="Convoy"/>: its cargo, its
/// border weight, its Goods Movement Reference and its ferry booking.
/// </summary>
/// <remarks>
/// A manifest is a child of the truck list — it is opened against a <see cref="ConvoyVehicle"/>,
/// never against a loose vehicle and convoy that might have nothing to do with each other. The
/// convoy is the unit that is planned; the manifest is the unit that is executed per vehicle.
///
/// <para>
/// It does <strong>not</strong> carry a crew. Who is driving is a fact about the vehicle on the
/// convoy, recorded once on the crew row and read from there — a manifest that kept its own driver
/// teams could, and did, disagree with the crew the insurance was bought for.
/// </para>
/// </remarks>
public class Manifest
{
    /// <summary>
    /// Unique reference — a document number read out at a border, not a surrogate key.
    /// </summary>
    public required ManifestId Id { get; init; }

    /// <summary>
    /// Where the manifest has reached in <see cref="ManifestTransitions"/>
    /// </summary>
    public ManifestStatus Status { get; init; } = ManifestStatus.Created;

    /// <summary>
    /// The truck-list entry this manifest is the paperwork for.
    /// </summary>
    public required ConvoyVehicle ConvoyVehicle { get; init; }

    /// <summary>
    /// The vehicle named by <see cref="ConvoyVehicle"/>, once it has been loaded. Null when only
    /// the reference is to hand — the weight then reads as cargo plus allowances.
    /// </summary>
    public Vehicle? Vehicle { get; init; }

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
        ManifestWeight.Total(this.Vehicle?.WeightKg ?? 0, this.Boxes.Sum(box => box.WeightKg));
}

/// <summary>
/// The weight a border check is given, and the fixed allowances that go into it.
/// </summary>
/// <remarks>
/// <strong>The padding is deliberate.</strong> 200 kg for two drivers and their bags and 45 kg for
/// fuel are the border-check estimate Ukrainian Action uses — docs/domain/key-concepts.md § Manifest
/// says so explicitly. Do not "correct" them without asking.
///
/// <para>
/// They live here, in one place, because they previously existed in three: the entity, the weight
/// query and the approval hand-off that composes the printed document. Three copies of an estimate
/// is three chances for the printed manifest, the API and the border document to disagree about
/// what the vehicle weighs.
/// </para>
/// </remarks>
public static class ManifestWeight
{
    /// <summary>Two drivers and their bags.</summary>
    public const int CrewAndBagsKg = 100 * 2;

    /// <summary>Fuel allowance.</summary>
    public const int FuelKg = 45;

    /// <summary>
    /// The vehicle's kerb weight plus its cargo plus the fixed allowances. A vehicle that is not
    /// yet known weighs zero, so a part-built manifest still reports a readable total.
    /// </summary>
    public static int Total(int vehicleKg, int cargoKg) => vehicleKg + cargoKg + CrewAndBagsKg + FuelKg;
}

/// <summary>
/// Unique reference to a manifest — the document number a border officer reads out.
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
