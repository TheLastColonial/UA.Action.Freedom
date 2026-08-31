using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Manifests;

/// <summary>
/// A manifest as this slice persists and returns it — the central document of the system, tying
/// one vehicle on one convoy to its driver teams and its cargo.
/// </summary>
/// <remarks>
/// <see cref="GmrSubmittedAt"/> is stamped at the moment the Goods Movement Reference is handed
/// to the customs worker, which is the point of no return: <c>docs/recommendations.md</c> §5.2
/// records the ruling that <em>once a GMR is created, no edits can be made to the manifest</em>.
/// From then on the only things that may still happen to it are the ones that describe what the
/// world did to the vehicle — delivered, lost, returned.
/// </remarks>
public sealed record ManifestReadModel(
    string Id,
    string? Vin,
    int? ConvoyId,
    ManifestStatus Status,
    string? DeliveryNotes,
    bool FerryBookingComplete,
    DateTime? GmrSubmittedAt)
{
    /// <summary>Whether the manifest can still be edited at all.</summary>
    public bool Frozen => this.GmrSubmittedAt is not null;
}

/// <summary>Which leg of the journey a driver team is crewing.</summary>
public enum ManifestLeg
{
    /// <summary>UK to Europe.</summary>
    Uk = 0,

    /// <summary>Europe to Ukraine.</summary>
    Border = 1,
}

/// <summary>
/// A driver team on one leg. A team may be half-crewed while it is being planned, so the
/// secondary driver is optional.
/// </summary>
public sealed record ManifestDriverTeamReadModel(
    ManifestLeg Leg,
    Guid PrimaryPersonId,
    Guid? SecondaryPersonId);

/// <summary>A box on the manifest, with enough of its state to add up a border weight.</summary>
public sealed record ManifestBoxReadModel(int BoxId, int WeightKg, bool Validated);

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
public sealed record ManifestWeightReadModel(
    int VehicleKg,
    int CargoKg,
    int CrewAndBagsKg,
    int FuelKg,
    int TotalKg,
    int UnvalidatedBoxCount);
