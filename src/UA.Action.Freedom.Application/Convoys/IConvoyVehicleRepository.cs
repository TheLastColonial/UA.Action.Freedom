using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>What <see cref="IConvoyVehicleRepository.AddAsync"/> found when it tried.</summary>
public enum AddToTruckListResult
{
    Added,
    VehicleNotFound,
    NotPassedInspection,
    OnAnotherConvoy,
    HandedOver,
    AlreadyOnThisConvoy
}

/// <summary>What <see cref="IConvoyVehicleRepository.AssignCrewAsync"/> found when it tried.</summary>
public enum AssignCrewResult
{
    Assigned,
    VehicleNotOnConvoy,
    AlreadyAssigned,
    OnAnotherVehicle
}

/// <summary>
/// Persistence port for the truck list — <c>dbo.ConvoyVehicle</c> — and the two things that hang
/// off an entry on it: the vehicle's crew and its insurance.
/// </summary>
/// <remarks>
/// Split out of <see cref="IConvoyRepository"/>, which had grown to twenty-one methods covering
/// two quite different things: the convoy's own journey, and the per-vehicle facts recorded against
/// it. The split follows the table boundary — everything here is keyed
/// <c>(ConvoyId, Vin)</c> — so a repository that only plans journeys cannot reach the crew.
///
/// <para>
/// The truck list is a table rather than a pointer on <c>dbo.Vehicle</c>. A pointer could hold only
/// the current convoy and was nulled at arrival, so an arrived convoy lost its own truck list while
/// the crew and insurance rows went on naming it; and a manifest's <c>(ConvoyId, Vin)</c> had
/// nothing to be a foreign key to.
/// </para>
/// </remarks>
public interface IConvoyVehicleRepository
{
    /// <summary>The whole truck list, withdrawn vehicles included, with crew counts per leg.</summary>
    Task<IReadOnlyList<ConvoyVehicleReadModel>> ListAsync(int convoyId, CancellationToken cancellationToken);

    /// <summary>One entry, or null when that vehicle is not on this convoy at all.</summary>
    Task<ConvoyVehicleReadModel?> GetAsync(int convoyId, string vin, CancellationToken cancellationToken);

    /// <summary>
    /// Puts the vehicle on the truck list, but only if it has passed its inspection, has not been
    /// handed over, and is not already travelling with a different convoy. The conditions are part
    /// of the write, so the database settles a race with a Mechanic changing the inspection result
    /// or with a second dispatcher.
    /// </summary>
    Task<AddToTruckListResult> AddAsync(int convoyId, string vin, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the vehicle off the list altogether, with its crew and insurance, in one transaction.
    /// Only legal before publication — afterwards a vehicle <em>withdraws</em>. Returns false when
    /// that vehicle is not on this convoy.
    /// </summary>
    Task<bool> RemoveAsync(int convoyId, string vin, CancellationToken cancellationToken);

    /// <summary>
    /// Records that the vehicle left the convoy mid-journey, keeping the row, its crew, its
    /// insurance and its manifest. Returns false when it is not on this convoy or has already
    /// withdrawn.
    /// </summary>
    /// <remarks>
    /// This is the breakdown case. The vehicle may be repaired and join a later convoy, or make its
    /// own way; either way its manifest goes on describing a load that is still real, which is why
    /// nothing here deletes anything.
    /// </remarks>
    Task<bool> WithdrawAsync(
        int convoyId, string vin, string? reason, DateTime withdrawnAt, CancellationToken cancellationToken);

    /// <summary>The crew of a vehicle on this convoy, or null when it is not on this convoy.</summary>
    Task<IReadOnlyList<VehicleCrewReadModel>?> ListCrewAsync(
        int convoyId, string vin, JourneyLeg? leg, CancellationToken cancellationToken);

    /// <summary>
    /// Puts a person on a vehicle's crew for one leg, provided the vehicle is on this convoy and
    /// the person is not already crewing another vehicle on that leg — one seat per person per leg.
    /// Voids the vehicle's insurance in the same transaction, because the policy names the crew.
    /// </summary>
    Task<AssignCrewResult> AssignCrewAsync(
        int convoyId, string vin, Guid personId, JourneyLeg leg, CrewRole role, CancellationToken cancellationToken);

    /// <summary>
    /// Takes a person off a vehicle's crew for one leg, voiding the insurance in the same
    /// transaction. Returns false when they were not crewing it.
    /// </summary>
    Task<bool> UnassignCrewAsync(
        int convoyId, string vin, Guid personId, JourneyLeg leg, CancellationToken cancellationToken);

    Task<VehicleInsuranceReadModel?> GetInsuranceAsync(int convoyId, string vin, CancellationToken cancellationToken);

    /// <summary>
    /// Records or replaces the insurance, clearing any void. Returns false when the vehicle is not
    /// on this convoy. Crew changes void it — see <see cref="AssignCrewAsync"/>.
    /// </summary>
    Task<bool> RecordInsuranceAsync(VehicleInsuranceRecord insurance, CancellationToken cancellationToken);

    Task<bool> RemoveInsuranceAsync(int convoyId, string vin, CancellationToken cancellationToken);
}
