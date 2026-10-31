namespace UA.Action.Freedom.Domain;

/// <summary>
/// The Administrator's sign-off of one <see cref="Vehicle"/>'s load on one <see cref="Convoy"/>, and the
/// document pack that goes with it: its cargo, its border weight and its delivery notes.
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
    public ManifestStatus Status { get; init; } = ManifestStatus.Proposed;

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
/// Where a manifest's load sign-off stands, as drawn in docs/manifest-status.puml (ADR 0004).
/// </summary>
public enum ManifestStatus
{
    /// <summary>Waiting for an Administrator to sign the load off. Also where a load change sends an approved manifest.</summary>
    Proposed = 0,

    /// <summary>The Administrator has signed the load off.</summary>
    Approved = 1,

    /// <summary>Sign-off was refused. Recoverable: it may be proposed again.</summary>
    Rejected = 2,
}

/// <summary>
/// The edges of the sign-off, as data so the diagram and the code can be read against each other.
/// </summary>
/// <remarks>
/// <see cref="ManifestStatus.Approved"/> to <see cref="ManifestStatus.Proposed"/> is the one edge nobody takes
/// by hand: it is how any later change to the load sends the sign-off back for re-approval (X3), and it is
/// written by the load change itself, in the same transaction, never by a <c>propose</c> request.
/// </remarks>
public static class ManifestTransitions
{
    private static readonly HashSet<(ManifestStatus From, ManifestStatus To)> Allowed =
    [
        (ManifestStatus.Proposed, ManifestStatus.Approved),
        (ManifestStatus.Proposed, ManifestStatus.Rejected),
        (ManifestStatus.Rejected, ManifestStatus.Proposed),
        (ManifestStatus.Approved, ManifestStatus.Proposed),
    ];

    /// <summary>Whether a manifest in <paramref name="from"/> may move to <paramref name="to"/>.</summary>
    public static bool CanTransition(ManifestStatus from, ManifestStatus to) => Allowed.Contains((from, to));

    /// <summary>Whether a person may ask for the move: every edge but the load change's own.</summary>
    public static bool CanBeRequested(ManifestStatus from, ManifestStatus to) =>
        CanTransition(from, to) && from != ManifestStatus.Approved;
}
