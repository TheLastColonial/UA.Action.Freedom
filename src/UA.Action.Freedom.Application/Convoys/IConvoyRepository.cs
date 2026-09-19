using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>What <see cref="IConvoyRepository.AssignVehicleAsync"/> found when it tried.</summary>
public enum AssignVehicleResult
{
    Assigned,
    VehicleNotFound,
    NotPassedInspection,
    OnAnotherConvoy,
    HandedOver
}

/// <summary>What <see cref="IConvoyRepository.ArriveAsync"/> found when it tried.</summary>
public enum ArriveResult
{
    Arrived,
    AlreadyArrived,
    VehiclesStillTravelling
}

/// <summary>What <see cref="IConvoyRepository.AssignDriverAsync"/> found when it tried.</summary>
public enum AssignDriverResult
{
    Assigned,
    VehicleNotOnConvoy,
    AlreadyAssigned,
    OnAnotherVehicle
}

/// <summary>
/// Persistence port for <see cref="ConvoyReadModel"/> and the two things a convoy owns: its
/// route and its truck list.
/// </summary>
/// <remarks>
/// The vehicle-assignment methods write <c>dbo.Vehicle.ConvoyId</c> rather than going through
/// <c>IVehicleRepository</c>. Which vehicles are travelling together is a fact about the convoy,
/// not about any one vehicle, and keeping it here is what lets the truck-list rule be enforced
/// in one place instead of on every vehicle write.
/// </remarks>
public interface IConvoyRepository
{
    Task<ConvoyReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConvoyReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken);

    /// <summary>Inserts a convoy and returns the identifier the database assigned.</summary>
    Task<int> AddAsync(DateTime start, DateTime expectedEnd, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(ConvoyReadModel convoy, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<RouteStopReadModel>> GetRouteAsync(int convoyId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the whole route in one transaction. A route is meaningful only as a complete
    /// ordered journey, so it is written whole rather than stop by stop.
    /// </summary>
    Task ReplaceRouteAsync(int convoyId, IReadOnlyList<RouteStopReadModel> stops, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConvoyVehicleReadModel>> ListVehiclesAsync(int convoyId, CancellationToken cancellationToken);

    /// <summary>
    /// Puts the vehicle on this convoy, but only if it has passed its inspection and is not
    /// already on a different one. The condition is part of the write, so the database settles a
    /// race with a Mechanic changing the result or with a second dispatcher.
    /// </summary>
    Task<AssignVehicleResult> AssignVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken);

    /// <summary>Returns false when that vehicle is not on this convoy.</summary>
    Task<bool> UnassignVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken);

    /// <summary>
    /// Stamps the publication time, but only if there is not one already. Returns false when the
    /// convoy does not exist <em>or</em> its truck list is already published — the caller
    /// distinguishes those by reading the convoy.
    /// </summary>
    Task<bool> PublishTruckListAsync(int convoyId, DateTime publishedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the drivers assigned to a specific vehicle on this convoy, or null when the convoy does not exist.
    /// </summary>
    Task<IReadOnlyList<VehicleDriverReadModel>?> ListVehicleDriversAsync(int convoyId, string vin, CancellationToken cancellationToken);

    /// <summary>
    /// Puts a person on a vehicle's crew in the given role, provided the vehicle is on this convoy
    /// and the person is not already crewing another vehicle of it — one seat per convoy.
    /// </summary>
    Task<AssignDriverResult> AssignDriverAsync(
        int convoyId, string vin, Guid personId, CrewRole role, CancellationToken cancellationToken);

    /// <summary>
    /// Unassigns a driver from a vehicle on a convoy. Returns false when there is no such vehicle
    /// on this convoy or the driver is not assigned to it — the convoy is part of the statement,
    /// not only of the handler's check.
    /// </summary>
    Task<bool> UnassignDriverAsync(int convoyId, string vin, Guid personId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the convoy arrived, provided its truck list is published, it has not arrived yet, and
    /// every vehicle on it has a finished manifest. In the same transaction, Delivered and Lost
    /// vehicles are handed over and Returned ones released.
    /// </summary>
    Task<ArriveResult> ArriveAsync(int convoyId, DateTime arrivedAt, CancellationToken cancellationToken);

    /// <summary>The VINs on this convoy with no Delivered, Lost or Returned manifest on it.</summary>
    Task<IReadOnlyList<string>> ListVehiclesStillTravellingAsync(int convoyId, CancellationToken cancellationToken);

    Task<VehicleInsuranceReadModel?> GetInsuranceAsync(int convoyId, string vin, CancellationToken cancellationToken);

    /// <summary>
    /// Records or replaces the insurance, clearing any void. Returns false when the vehicle is not
    /// on this convoy. Crew changes void it — see <see cref="AssignDriverAsync"/>.
    /// </summary>
    Task<bool> RecordInsuranceAsync(VehicleInsuranceRecord insurance, CancellationToken cancellationToken);

    Task<bool> RemoveInsuranceAsync(int convoyId, string vin, CancellationToken cancellationToken);
}
