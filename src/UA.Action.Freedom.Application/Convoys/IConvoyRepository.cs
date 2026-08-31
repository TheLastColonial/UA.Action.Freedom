namespace UA.Action.Freedom.Application.Convoys;

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

    /// <summary>Returns false when there is no vehicle with that VIN.</summary>
    Task<bool> AssignVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken);

    /// <summary>Returns false when that vehicle is not on this convoy.</summary>
    Task<bool> UnassignVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken);

    /// <summary>
    /// Stamps the publication time, but only if there is not one already. Returns false when the
    /// convoy does not exist <em>or</em> its truck list is already published — the caller
    /// distinguishes those by reading the convoy.
    /// </summary>
    Task<bool> PublishTruckListAsync(int convoyId, DateTime publishedAt, CancellationToken cancellationToken);
}
