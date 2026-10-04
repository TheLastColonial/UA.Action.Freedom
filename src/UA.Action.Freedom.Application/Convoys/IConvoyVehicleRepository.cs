using UA.Action.Freedom.Application.Manifests;
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

/// <summary>What <see cref="IConvoyVehicleRepository.AllocateBoxAsync"/> found when it tried.</summary>
public enum AllocateBoxResult
{
    Allocated,
    Moved,
    AlreadyAllocated,
    VehicleNotOnConvoy,
    VehicleWithdrawn,
    BoxNotFound
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
    /// <summary>The whole truck list, withdrawn vehicles included, with its driver and passenger counts.</summary>
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
    /// Records the Receiver this vehicle is handed over to in Ukraine. Returns false when the vehicle is not
    /// on the convoy. The caller has already checked the Receiver is registered.
    /// </summary>
    Task<bool> SetHandoverReceiverAsync(
        int convoyId, string vin, Guid receiverRef, CancellationToken cancellationToken);

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
        int convoyId, string vin, CancellationToken cancellationToken);

    /// <summary>
    /// Puts a person on a vehicle's crew, provided the vehicle is on this convoy and the person is
    /// not already crewing a vehicle on it — one seat per person per convoy. A driver added after the
    /// insurance was recorded is uncovered until it is recorded again.
    /// </summary>
    Task<AssignCrewResult> AssignCrewAsync(
        int convoyId, string vin, Guid personId, CrewRole role, CancellationToken cancellationToken);

    /// <summary>
    /// Takes a person off a vehicle's crew, and off the insurance's covered drivers in the same
    /// transaction; the policy stays in cover for the rest. Returns false when they were not crewing it.
    /// </summary>
    Task<bool> UnassignCrewAsync(
        int convoyId, string vin, Guid personId, CancellationToken cancellationToken);

    Task<VehicleInsuranceReadModel?> GetInsuranceAsync(int convoyId, string vin, CancellationToken cancellationToken);

    /// <summary>
    /// Records or replaces the insurance, clearing any void, and covers every driver the vehicle has
    /// at that moment. Returns false when the vehicle is not on this convoy.
    /// </summary>
    Task<bool> RecordInsuranceAsync(VehicleInsuranceRecord insurance, CancellationToken cancellationToken);

    Task<bool> RemoveInsuranceAsync(int convoyId, string vin, CancellationToken cancellationToken);

    /// <summary>
    /// The cargo on a vehicle, meaning the boxes allocated to its entry, or null when it is not on this convoy.
    /// </summary>
    Task<IReadOnlyList<ManifestBoxReadModel>?> ListBoxesAsync(
        int convoyId, string vin, CancellationToken cancellationToken);

    /// <summary>Where a box is allocated now, or null when it is on no vehicle.</summary>
    Task<BoxAllocation?> GetBoxAllocationAsync(int boxId, CancellationToken cancellationToken);

    /// <summary>
    /// Puts a box on a vehicle, moving it if it was on another: a box is on at most one entry. The
    /// entry must be on this convoy and still travelling, and the box must exist; the move is one
    /// transaction, so a box is never on two vehicles nor on none in between.
    /// </summary>
    Task<AllocateBoxResult> AllocateBoxAsync(
        int convoyId, string vin, int boxId, CancellationToken cancellationToken);

    /// <summary>Takes a box off this vehicle. Returns false when it was not allocated to it.</summary>
    Task<bool> RemoveBoxAsync(int convoyId, string vin, int boxId, CancellationToken cancellationToken);
}
