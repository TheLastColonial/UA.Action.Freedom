namespace UA.Action.Freedom.Application.Convoys;

/// <summary>
/// What is written when a booking is made or replaced. Guests are people by id only: no name is ever stored on a
/// booking, so erasing a volunteer leaves nothing of them in its details.
/// </summary>
public sealed record AccommodationBookingRecord(
    int ConvoyId,
    int RoutePointId,
    string Provider,
    string? Reference,
    DateTime CheckIn,
    DateTime CheckOut,
    string? Details,
    decimal? CostGbp,
    IReadOnlyList<Guid> Guests);

public sealed record AccommodationBookingReadModel(
    int Id,
    int ConvoyId,
    int RoutePointId,
    string Provider,
    string? Reference,
    DateTime CheckIn,
    DateTime CheckOut,
    string? Details,
    decimal? CostGbp,
    bool Cancelled,
    IReadOnlyList<Guid> Guests,
    string? LastChangedByName = null,
    DateTime? LastChangedAt = null);

/// <summary>A crew member arranging their own accommodation at one overnight stop (O4, O30).</summary>
public sealed record SelfAccommodationReadModel(
    int ConvoyId,
    int RoutePointId,
    Guid PersonId,
    string? LastChangedByName = null,
    DateTime? LastChangedAt = null);

public enum ReplaceBookingResult
{
    Replaced,
    NotFound,
    Cancelled,
    PointNotOnConvoy,
}

public enum MigrateGuestResult
{
    Migrated,
    NotFound,
    Cancelled,
    NotAGuest,
}

/// <summary>
/// Persistence port for a convoy's accommodation: the bookings at its route points, the crew each covers, and the
/// crew who arrange their own. Whether a person is crewed is a rule of the handlers, not of this store: a booking
/// outlives its guest leaving the crew (P13).
/// </summary>
public interface IAccommodationRepository
{
    /// <summary>Every booking of the convoy, cancelled ones included, each with its guests.</summary>
    Task<IReadOnlyList<AccommodationBookingReadModel>> ListBookingsAsync(int convoyId, CancellationToken cancellationToken);

    Task<AccommodationBookingReadModel?> GetBookingAsync(int convoyId, int bookingId, CancellationToken cancellationToken);

    /// <summary>
    /// Makes the booking and returns its identifier, or null when the route point is not on this convoy. The
    /// booking and its guests are one transaction.
    /// </summary>
    Task<int?> AddBookingAsync(AccommodationBookingRecord booking, CancellationToken cancellationToken);

    /// <summary>Replaces a booking that has not been cancelled, its guests with it.</summary>
    Task<ReplaceBookingResult> ReplaceBookingAsync(
        int bookingId, AccommodationBookingRecord booking, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels the booking: it is stamped, not deleted, because the spend and who made it stay on record. Cancelling
    /// twice is not an error. Returns false when there is no such booking on this convoy.
    /// </summary>
    Task<bool> CancelBookingAsync(int convoyId, int bookingId, CancellationToken cancellationToken);

    /// <summary>
    /// Moves one guest's place on a booking to another person, in one transaction. A person already on the booking
    /// simply keeps their place and the departed guest's goes.
    /// </summary>
    Task<MigrateGuestResult> MigrateGuestAsync(
        int convoyId, int bookingId, Guid fromPersonId, Guid toPersonId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SelfAccommodationReadModel>> ListSelfAsync(int convoyId, CancellationToken cancellationToken);

    /// <summary>Flags the person as arranging their own stay at the stop. Idempotent; false when the point is not on this convoy.</summary>
    Task<bool> SetSelfAsync(int convoyId, int routePointId, Guid personId, CancellationToken cancellationToken);

    /// <summary>Removes the flag. Returns false when it was not set.</summary>
    Task<bool> RemoveSelfAsync(int convoyId, int routePointId, Guid personId, CancellationToken cancellationToken);
}
