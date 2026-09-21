using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>What <see cref="IConvoyRepository.ArriveAsync"/> found when it tried.</summary>
public enum ArriveResult
{
    Arrived,
    AlreadyArrived,
    VehiclesStillTravelling
}

/// <summary>
/// Persistence port for <see cref="ConvoyReadModel"/>, its route, and the journey's end.
/// </summary>
/// <remarks>
/// The truck list, the crew and the insurance moved to <see cref="IConvoyVehicleRepository"/> when
/// the truck list became a table of its own. What is left here is the convoy as a journey: when it
/// leaves, where it goes, when the list closes, and when it arrives.
/// </remarks>
public interface IConvoyRepository
{
    Task<ConvoyReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConvoyReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken);

    /// <summary>Inserts a convoy and returns the identifier the database assigned.</summary>
    Task<int> AddAsync(DateTime start, DateTime expectedEnd, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(ConvoyReadModel convoy, CancellationToken cancellationToken);

    /// <summary>Refused (<see cref="DeleteResult.StillReferenced"/>) while a manifest names the convoy.</summary>
    Task<DeleteResult> DeleteAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<RouteStopReadModel>> GetRouteAsync(int convoyId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the whole route in one transaction. A route is meaningful only as a complete
    /// ordered journey, so it is written whole rather than stop by stop.
    /// </summary>
    Task ReplaceRouteAsync(int convoyId, IReadOnlyList<RouteStopReadModel> stops, CancellationToken cancellationToken);

    /// <summary>
    /// Stamps the publication time, but only if there is not one already. Returns false when the
    /// convoy does not exist <em>or</em> its truck list is already published — the caller
    /// distinguishes those by reading the convoy.
    /// </summary>
    Task<bool> PublishTruckListAsync(int convoyId, DateTime publishedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the convoy arrived, provided its truck list is published, it has not arrived yet, and
    /// every vehicle still travelling with it has a finished manifest. In the same transaction,
    /// Delivered and Lost vehicles are stamped handed over.
    /// </summary>
    /// <remarks>
    /// Withdrawn vehicles are not waited for: one that broke down near Poznan is not going to
    /// deliver anything, and holding the whole convoy open for it would mean it could never arrive.
    /// Nor is anything released — a vehicle that was not handed over is free to travel again
    /// because the convoy has arrived, not because a pointer was cleared.
    /// </remarks>
    Task<ArriveResult> ArriveAsync(int convoyId, DateTime arrivedAt, CancellationToken cancellationToken);

    /// <summary>
    /// The VINs still travelling with this convoy that have no Delivered, Lost or Returned manifest.
    /// </summary>
    Task<IReadOnlyList<string>> ListVehiclesStillTravellingAsync(int convoyId, CancellationToken cancellationToken);
}
