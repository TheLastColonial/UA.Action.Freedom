using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Manifests;

/// <summary>
/// A manifest as this slice persists and returns it — the document pack for one vehicle on one
/// convoy: its cargo, its border weight, its Goods Movement Reference and its ferry booking.
/// </summary>
/// <remarks>
/// <see cref="ConvoyId"/> and <see cref="Vin"/> are not optional and are not independently
/// editable: together they are the truck-list entry this manifest is the paperwork for, and the
/// database holds them as a composite foreign key to it. They used to be two loose nullable
/// columns, so nothing checked the vehicle was on that convoy and nothing stopped one vehicle
/// carrying two manifests.
///
/// <para>
/// There is no crew here. Who is driving is a fact about the vehicle on the convoy, read from the
/// one crew record rather than kept a second time — see <c>VehicleCrewReadModel</c>.
/// </para>
///
/// <para>
/// <see cref="GmrSubmittedAt"/> is stamped at the moment the Goods Movement Reference is handed
/// to the customs worker, which is the point of no return: <c>docs/recommendations.md</c> §5.2
/// records the ruling that <em>once a GMR is created, no edits can be made to the manifest</em>.
/// From then on the only things that may still happen to it are the ones that describe what the
/// world did to the vehicle — delivered, lost, returned.
/// </para>
/// </remarks>
public sealed record ManifestReadModel(
    string Id,
    int ConvoyId,
    string Vin,
    ManifestStatus Status,
    string? DeliveryNotes,
    bool FerryBookingComplete,
    DateTime? GmrSubmittedAt)
{
    /// <summary>Whether the manifest can still be edited at all.</summary>
    public bool Frozen => this.GmrSubmittedAt is not null;
}

/// <summary>A box on the manifest, with enough of its state to add up a border weight.</summary>
/// <remarks>
/// <see cref="WidthCm"/>, <see cref="DepthCm"/> and <see cref="HeightCm"/> are set alongside the
/// box's confirmed weight at validation, and default to null for callers that do not care about
/// them (most existing tests): a box with no recorded dimensions simply cannot be judged
/// oversized (see <see cref="GetManifestWeightHandler"/>).
/// </remarks>
public sealed record ManifestBoxReadModel(
    int BoxId, int WeightKg, bool Validated,
    decimal? WidthCm = null, decimal? DepthCm = null, decimal? HeightCm = null);

/// <summary>
/// The manifest's vehicle's cargo capacity, or all-null when no vehicle is assigned yet or the
/// vehicle has never had its capacity measured.
/// </summary>
public sealed record VehicleCargoCapacityReadModel(
    decimal? MaxCargoWeightKg, decimal? CargoWidthCm, decimal? CargoDepthCm, decimal? CargoHeightCm);

/// <summary>
/// The weight a border check is given, broken into its parts.
/// </summary>
/// <remarks>
/// The breakdown is returned rather than a single number so the fixed allowances are visible.
/// <see cref="CrewAndBagsKg"/> (two drivers and their bags) and <see cref="FuelKg"/> are a
/// deliberate border-check estimate, not a bug — docs/domain/key-concepts.md says so explicitly,
/// and a lone total invites someone to "correct" them.
///
/// <see cref="UnvalidatedBoxCount"/> is the honesty flag: cargo weight only means anything for
/// boxes a Loader has actually weighed, so a total containing unvalidated boxes is provisional
/// and says so.
/// </remarks>
/// <remarks>
/// <see cref="MaxCargoWeightKg"/>, <see cref="CargoOverweight"/> and
/// <see cref="OversizedBoxIds"/> are advisory only: this endpoint never rejects anything, and a
/// vehicle or box with no capacity/dimension data recorded simply cannot be flagged (see
/// <see cref="GetManifestWeightHandler"/>).
/// </remarks>
public sealed record ManifestWeightReadModel(
    int VehicleKg,
    int CargoKg,
    int CrewAndBagsKg,
    int FuelKg,
    int TotalKg,
    int UnvalidatedBoxCount,
    decimal? MaxCargoWeightKg,
    bool CargoOverweight,
    IReadOnlyList<int> OversizedBoxIds);
