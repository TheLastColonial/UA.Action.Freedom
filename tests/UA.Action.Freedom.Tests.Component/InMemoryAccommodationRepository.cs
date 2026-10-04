using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// In-memory stand-in for <c>AccommodationRepository</c>. It mirrors the SQL: a booking or a flag needs a route point
/// of this convoy, a cancelled booking can be neither replaced nor migrated, cancelling twice is fine, a migration
/// moves one place in one step, and a convoy's rows go with the convoy. It also tells the convoy fake which route
/// points it refers to, as the foreign keys do, so an edit of the route cannot remove one.
/// </summary>
internal sealed class InMemoryAccommodationRepository : IAccommodationRepository, IRecordsWhoChanged
{
    private readonly InMemoryConvoyRepository? convoys;
    private readonly ChangeLedger<string> changes = new();
    private readonly List<StoredBooking> bookings = [];
    private readonly List<(int ConvoyId, int RoutePointId, Guid PersonId)> selfArranged = [];
    private int nextId = 1;

    private sealed record StoredBooking(int Id, AccommodationBookingRecord Record, bool Cancelled, List<Guid> Guests);

    public InMemoryAccommodationRepository(InMemoryConvoyRepository? convoys = null)
    {
        this.convoys = convoys;

        if (convoys is not null)
        {
            convoys.AlsoReferenced = (convoyId, ids) =>
                Live(bookings.Where(booking => booking.Record.ConvoyId == convoyId)).Any(booking => ids.Contains(booking.Record.RoutePointId))
                || selfArranged.Exists(flag => flag.ConvoyId == convoyId && ids.Contains(flag.RoutePointId));
        }
    }

    public void Attach(IChangeAttribution attribution, IPersonRepository people) => changes.Attach(attribution, people);

    private static string BookingKey(int id) => $"booking/{id}";

    private static string SelfKey(int convoyId, int routePointId, Guid personId) => $"self/{convoyId}/{routePointId}/{personId}";

    private bool ConvoyExists(int convoyId) =>
        convoys is null || convoys.GetByIdAsync(convoyId, CancellationToken.None).GetAwaiter().GetResult() is not null;

    private IEnumerable<StoredBooking> Live(IEnumerable<StoredBooking> rows) =>
        rows.Where(booking => ConvoyExists(booking.Record.ConvoyId));

    private bool PointIsOnConvoy(int convoyId, int routePointId) =>
        convoys is null || convoys.RouteOf(convoyId).Any(stop => stop.RoutePointId == routePointId);

    private AccommodationBookingReadModel Read(StoredBooking booking)
    {
        var (name, at) = changes.Of(BookingKey(booking.Id));
        var record = booking.Record;
        return new AccommodationBookingReadModel(
            booking.Id, record.ConvoyId, record.RoutePointId, record.Provider, record.Reference, record.CheckIn,
            record.CheckOut, record.Details, record.CostGbp, booking.Cancelled, [.. booking.Guests.Order()], name, at);
    }

    public Task<IReadOnlyList<AccommodationBookingReadModel>> ListBookingsAsync(int convoyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AccommodationBookingReadModel>>(
            [.. Live(bookings.Where(booking => booking.Record.ConvoyId == convoyId))
                .OrderBy(booking => booking.Record.RoutePointId).ThenBy(booking => booking.Id)
                .Select(Read)]);

    public Task<AccommodationBookingReadModel?> GetBookingAsync(int convoyId, int bookingId, CancellationToken cancellationToken) =>
        Task.FromResult(
            Live(bookings.Where(booking => booking.Record.ConvoyId == convoyId && booking.Id == bookingId))
                .Select(Read).SingleOrDefault());

    public Task<int?> AddBookingAsync(AccommodationBookingRecord booking, CancellationToken cancellationToken)
    {
        if (!PointIsOnConvoy(booking.ConvoyId, booking.RoutePointId))
        {
            return Task.FromResult<int?>(null);
        }

        var id = nextId++;
        bookings.Add(new StoredBooking(id, booking, false, [.. booking.Guests.Distinct()]));
        changes.Stamp(BookingKey(id));
        return Task.FromResult<int?>(id);
    }

    public Task<ReplaceBookingResult> ReplaceBookingAsync(
        int bookingId, AccommodationBookingRecord booking, CancellationToken cancellationToken)
    {
        var index = bookings.FindIndex(row => row.Id == bookingId && row.Record.ConvoyId == booking.ConvoyId);
        if (index < 0)
        {
            return Task.FromResult(ReplaceBookingResult.NotFound);
        }

        if (bookings[index].Cancelled)
        {
            return Task.FromResult(ReplaceBookingResult.Cancelled);
        }

        if (!PointIsOnConvoy(booking.ConvoyId, booking.RoutePointId))
        {
            return Task.FromResult(ReplaceBookingResult.PointNotOnConvoy);
        }

        bookings[index] = bookings[index] with { Record = booking, Guests = [.. booking.Guests.Distinct()] };
        changes.Stamp(BookingKey(bookingId));
        return Task.FromResult(ReplaceBookingResult.Replaced);
    }

    public Task<bool> CancelBookingAsync(int convoyId, int bookingId, CancellationToken cancellationToken)
    {
        var index = bookings.FindIndex(row => row.Id == bookingId && row.Record.ConvoyId == convoyId);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        if (!bookings[index].Cancelled)
        {
            bookings[index] = bookings[index] with { Cancelled = true };
            changes.Stamp(BookingKey(bookingId));
        }

        return Task.FromResult(true);
    }

    public Task<MigrateGuestResult> MigrateGuestAsync(
        int convoyId, int bookingId, Guid fromPersonId, Guid toPersonId, CancellationToken cancellationToken)
    {
        var booking = bookings.Find(row => row.Id == bookingId && row.Record.ConvoyId == convoyId);
        if (booking is null)
        {
            return Task.FromResult(MigrateGuestResult.NotFound);
        }

        if (booking.Cancelled)
        {
            return Task.FromResult(MigrateGuestResult.Cancelled);
        }

        if (!booking.Guests.Remove(fromPersonId))
        {
            return Task.FromResult(MigrateGuestResult.NotAGuest);
        }

        if (!booking.Guests.Contains(toPersonId))
        {
            booking.Guests.Add(toPersonId);
        }

        changes.Stamp(BookingKey(bookingId));
        return Task.FromResult(MigrateGuestResult.Migrated);
    }

    public Task<IReadOnlyList<SelfAccommodationReadModel>> ListSelfAsync(int convoyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SelfAccommodationReadModel>>(
            ConvoyExists(convoyId)
                ? [.. selfArranged
                    .Where(flag => flag.ConvoyId == convoyId)
                    .OrderBy(flag => flag.RoutePointId).ThenBy(flag => flag.PersonId)
                    .Select(flag =>
                    {
                        var (name, at) = changes.Of(SelfKey(flag.ConvoyId, flag.RoutePointId, flag.PersonId));
                        return new SelfAccommodationReadModel(flag.ConvoyId, flag.RoutePointId, flag.PersonId, name, at);
                    })]
                : []);

    public Task<bool> SetSelfAsync(int convoyId, int routePointId, Guid personId, CancellationToken cancellationToken)
    {
        if (!PointIsOnConvoy(convoyId, routePointId))
        {
            return Task.FromResult(false);
        }

        var flag = (convoyId, routePointId, personId);
        if (!selfArranged.Contains(flag))
        {
            selfArranged.Add(flag);
            changes.Stamp(SelfKey(convoyId, routePointId, personId));
        }

        return Task.FromResult(true);
    }

    public Task<bool> RemoveSelfAsync(int convoyId, int routePointId, Guid personId, CancellationToken cancellationToken)
    {
        changes.Forget(SelfKey(convoyId, routePointId, personId));
        return Task.FromResult(selfArranged.Remove((convoyId, routePointId, personId)));
    }
}
