namespace UA.Action.Freedom.Domain;

/// <summary>How one crew member is covered at one overnight stop (P8, O4).</summary>
public enum CoverageStatus
{
    Booked,
    SelfArranged,
    Missing,
}

/// <summary>
/// A booking at a route point for one or more crew members who share. Guests are people by id only: a booking
/// never carries a name, so erasing a volunteer leaves nothing of them here.
/// </summary>
public sealed record AccommodationBooking(
    int Id, int RoutePointId, IReadOnlyCollection<Guid> Guests, bool Cancelled = false);

/// <summary>A crew member arranging their own accommodation at an overnight stop (O4, O30).</summary>
public sealed record SelfArrangement(int RoutePointId, Guid PersonId);

public sealed record CoverageCell(int RoutePointId, Guid PersonId, CoverageStatus Status);

public static class Accommodation
{
    /// <summary>
    /// One cell per overnight stop per crew member, in route order. A stop that is not flagged overnight needs
    /// nothing. A booking covers its guests at its own stop only; a cancelled one covers nobody.
    /// </summary>
    public static IReadOnlyList<CoverageCell> Coverage(
        IEnumerable<RoutePoint> routePoints,
        IReadOnlyCollection<Guid> crew,
        IReadOnlyCollection<AccommodationBooking> bookings,
        IReadOnlyCollection<SelfArrangement> selfArranged)
    {
        var booked = bookings
            .Where(booking => !booking.Cancelled)
            .SelectMany(booking => booking.Guests.Select(guest => (booking.RoutePointId, PersonId: guest)))
            .ToHashSet();

        var own = selfArranged.Select(flag => (flag.RoutePointId, flag.PersonId)).ToHashSet();

        return routePoints
            .Where(point => point.Kind == RoutePointKind.Overnight)
            .SelectMany(point => crew.Select(person => new CoverageCell(
                point.Id,
                person,
                StatusOf((point.Id, person), booked, own))))
            .ToList();
    }

    /// <summary>Bookings still standing that cover nobody currently crewed (P13): the Dispatcher cancels or migrates them.</summary>
    public static IReadOnlyList<AccommodationBooking> LeftoverBookings(
        IEnumerable<AccommodationBooking> bookings, IReadOnlyCollection<Guid> crew) =>
        bookings.Where(booking => !booking.Cancelled && !booking.Guests.Any(crew.Contains)).ToList();

    private static CoverageStatus StatusOf(
        (int RoutePointId, Guid PersonId) cell,
        HashSet<(int RoutePointId, Guid PersonId)> booked,
        HashSet<(int RoutePointId, Guid PersonId)> own) =>
        booked.Contains(cell) ? CoverageStatus.Booked
        : own.Contains(cell) ? CoverageStatus.SelfArranged
        : CoverageStatus.Missing;
}
