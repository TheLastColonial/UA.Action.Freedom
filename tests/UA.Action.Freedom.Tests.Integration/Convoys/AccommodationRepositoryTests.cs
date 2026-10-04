using AwesomeAssertions;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Data.Convoys;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.Convoys.ConvoyFixtures;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Convoys;

/// <summary>
/// Accommodation against a real database: the bookings at a route point, who they cover, the people who arrange
/// their own, and the route point a booking stops an edit of the route from removing (P2, P13, O4).
/// </summary>
[Trait("Category", "Integration")]
public class AccommodationRepositoryTests
{
    private const string AccommodationProbe =
        """
        SELECT COUNT(1) FROM dbo.AccommodationBooking;
        SELECT COUNT(1) FROM dbo.AccommodationBookingGuest;
        SELECT COUNT(1) FROM dbo.SelfAccommodation;
        """;

    private static readonly DateTime CheckIn = new(2026, 9, 2, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CheckOut = new(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc);

    private sealed record Arrangement(
        ConvoyRepository Convoys,
        AccommodationRepository Accommodation,
        AccommodationRoutePointReferences References);

    private static async Task<Arrangement> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync(Probe + AccommodationProbe, cancellationToken);
        return new Arrangement(
            new ConvoyRepository(ConnectionFactory(), Unattributed),
            new AccommodationRepository(ConnectionFactory(), Unattributed),
            new AccommodationRoutePointReferences(ConnectionFactory()));
    }

    private static RouteStopReadModel AnOvernight(string name, int sequence, int id = 0) =>
        new(sequence, null, null, name, "France", "", "FR", id, name, RoutePointKind.Overnight);

    private static AccommodationBookingRecord ABooking(
        int convoyId, int routePointId, string provider = "Ibis Lille", decimal? cost = 120m, params Guid[] guests) =>
        new(convoyId, routePointId, provider, "REF-1", CheckIn, CheckOut, "Two twin rooms", cost, guests);

    private static async Task<IReadOnlyList<int>> RouteOfAsync(
        ConvoyRepository convoys, int convoyId, params string[] names)
    {
        await convoys.ReplaceRouteAsync(
            convoyId, [.. names.Select((name, index) => AnOvernight(name, index + 1))], CancellationToken.None);
        return (await convoys.GetRouteAsync(convoyId, CancellationToken.None)).Select(stop => stop.RoutePointId).ToList();
    }

    private static Task CleanUpAsync(int convoyId, params Guid[] people) => RemoveAccommodationAsync(convoyId);

    [Fact]
    public async Task A_booking_round_trips_with_its_guests_and_the_person_who_made_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SkipUnlessReachableAsync(Probe + AccommodationProbe, cancellationToken);
        var dora = await AddVolunteerAsync("Dora", "Dispatcher", isDriver: false);
        var anna = await AddDriverAsync("Anna", "One");
        var boris = await AddDriverAsync("Boris", "Two");
        var convoys = new ConvoyRepository(ConnectionFactory(), Unattributed);
        var accommodation = new AccommodationRepository(ConnectionFactory(), AttributedTo(dora));
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            var points = await RouteOfAsync(convoys, id, "Lille");

            var bookingId = await accommodation.AddBookingAsync(
                ABooking(id, points[0], guests: [anna, boris]), cancellationToken);

            var booking = await accommodation.GetBookingAsync(id, bookingId!.Value, cancellationToken);
            booking.Should().NotBeNull();
            booking.Provider.Should().Be("Ibis Lille");
            booking.CostGbp.Should().Be(120m);
            booking.CheckIn.Should().Be(CheckIn);
            booking.Cancelled.Should().BeFalse();
            booking.Guests.Should().BeEquivalentTo([anna, boris]);
            booking.LastChangedByName.Should().Be("Dora Dispatcher");
            (await accommodation.ListBookingsAsync(id, cancellationToken)).Should().ContainSingle();
        }
        finally
        {
            await CleanUpAsync(id, dora, anna, boris);
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(dora, anna, boris);
        }
    }

    [Fact]
    public async Task A_booking_at_a_point_of_another_convoy_is_refused_and_writes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, accommodation, _) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var other = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            var otherPoints = await RouteOfAsync(convoys, other, "Calais");

            var bookingId = await accommodation.AddBookingAsync(ABooking(id, otherPoints[0]), cancellationToken);

            bookingId.Should().BeNull();
            (await accommodation.ListBookingsAsync(id, cancellationToken)).Should().BeEmpty();
        }
        finally
        {
            await RemoveConvoyAsync(id);
            await RemoveConvoyAsync(other);
        }
    }

    [Fact]
    public async Task Replacing_a_booking_replaces_its_guests_and_a_cancelled_booking_cannot_be_replaced()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, accommodation, _) = await ConnectOrSkipAsync(cancellationToken);
        var anna = await AddDriverAsync("Anna", "One");
        var boris = await AddDriverAsync("Boris", "Two");
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            var points = await RouteOfAsync(convoys, id, "Lille", "Reims");
            var bookingId = (await accommodation.AddBookingAsync(ABooking(id, points[0], guests: [anna]), cancellationToken))!.Value;

            (await accommodation.ReplaceBookingAsync(
                bookingId, ABooking(id, points[1], "Hotel Reims", 90m, boris), cancellationToken))
                .Should().Be(ReplaceBookingResult.Replaced);

            var booking = (await accommodation.GetBookingAsync(id, bookingId, cancellationToken))!;
            booking.Should().Match<AccommodationBookingReadModel>(b => b.Provider == "Hotel Reims" && b.RoutePointId == points[1]);
            booking.Guests.Should().Equal(boris);

            await accommodation.CancelBookingAsync(id, bookingId, cancellationToken);
            (await accommodation.ReplaceBookingAsync(bookingId, ABooking(id, points[0]), cancellationToken))
                .Should().Be(ReplaceBookingResult.Cancelled);
            (await accommodation.ReplaceBookingAsync(bookingId + 1000, ABooking(id, points[0]), cancellationToken))
                .Should().Be(ReplaceBookingResult.NotFound);
        }
        finally
        {
            await CleanUpAsync(id, anna, boris);
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(anna, boris);
        }
    }

    [Fact]
    public async Task Cancelling_keeps_the_booking_and_its_cost_and_is_not_an_error_twice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, accommodation, _) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            var points = await RouteOfAsync(convoys, id, "Lille");
            var bookingId = (await accommodation.AddBookingAsync(ABooking(id, points[0]), cancellationToken))!.Value;

            (await accommodation.CancelBookingAsync(id, bookingId, cancellationToken)).Should().BeTrue();
            (await accommodation.CancelBookingAsync(id, bookingId, cancellationToken)).Should().BeTrue();
            (await accommodation.CancelBookingAsync(id, bookingId + 1000, cancellationToken)).Should().BeFalse();
            (await accommodation.CancelBookingAsync(id + 1000, bookingId, cancellationToken)).Should().BeFalse();

            var booking = (await accommodation.GetBookingAsync(id, bookingId, cancellationToken))!;
            booking.Cancelled.Should().BeTrue();
            booking.CostGbp.Should().Be(120m);
        }
        finally
        {
            await CleanUpAsync(id);
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Migrating_moves_one_guests_place_to_another_person_in_one_step()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, accommodation, _) = await ConnectOrSkipAsync(cancellationToken);
        var anna = await AddDriverAsync("Anna", "One");
        var boris = await AddDriverAsync("Boris", "Two");
        var carla = await AddDriverAsync("Carla", "Three");
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            var points = await RouteOfAsync(convoys, id, "Lille");
            var bookingId = (await accommodation.AddBookingAsync(ABooking(id, points[0], guests: [anna, boris]), cancellationToken))!.Value;

            (await accommodation.MigrateGuestAsync(id, bookingId, anna, carla, cancellationToken))
                .Should().Be(MigrateGuestResult.Migrated);
            (await accommodation.GetBookingAsync(id, bookingId, cancellationToken))!.Guests
                .Should().BeEquivalentTo([boris, carla]);

            (await accommodation.MigrateGuestAsync(id, bookingId, boris, carla, cancellationToken))
                .Should().Be(MigrateGuestResult.Migrated);
            (await accommodation.GetBookingAsync(id, bookingId, cancellationToken))!.Guests.Should().Equal(carla);

            (await accommodation.MigrateGuestAsync(id, bookingId, anna, boris, cancellationToken))
                .Should().Be(MigrateGuestResult.NotAGuest);
            (await accommodation.MigrateGuestAsync(id, bookingId + 1000, carla, boris, cancellationToken))
                .Should().Be(MigrateGuestResult.NotFound);

            await accommodation.CancelBookingAsync(id, bookingId, cancellationToken);
            (await accommodation.MigrateGuestAsync(id, bookingId, carla, boris, cancellationToken))
                .Should().Be(MigrateGuestResult.Cancelled);
        }
        finally
        {
            await CleanUpAsync(id, anna, boris, carla);
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(anna, boris, carla);
        }
    }

    [Fact]
    public async Task A_self_accommodation_flag_is_set_once_listed_and_removed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, accommodation, _) = await ConnectOrSkipAsync(cancellationToken);
        var anna = await AddDriverAsync("Anna", "One");
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            var points = await RouteOfAsync(convoys, id, "Lille");

            (await accommodation.SetSelfAsync(id, points[0], anna, cancellationToken)).Should().BeTrue();
            (await accommodation.SetSelfAsync(id, points[0], anna, cancellationToken)).Should().BeTrue();
            (await accommodation.SetSelfAsync(id, points[0] + 1000, anna, cancellationToken)).Should().BeFalse();

            (await accommodation.ListSelfAsync(id, cancellationToken)).Select(flag => (flag.RoutePointId, flag.PersonId))
                .Should().Equal((points[0], anna));

            (await accommodation.RemoveSelfAsync(id, points[0], anna, cancellationToken)).Should().BeTrue();
            (await accommodation.RemoveSelfAsync(id, points[0], anna, cancellationToken)).Should().BeFalse();
            (await accommodation.ListSelfAsync(id, cancellationToken)).Should().BeEmpty();
        }
        finally
        {
            await CleanUpAsync(id, anna);
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(anna);
        }
    }

    [Fact]
    public async Task A_route_point_a_booking_or_a_flag_stays_at_is_reported_as_in_use_even_when_cancelled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, accommodation, references) = await ConnectOrSkipAsync(cancellationToken);
        var anna = await AddDriverAsync("Anna", "One");
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            var points = await RouteOfAsync(convoys, id, "Lille", "Reims", "Metz");
            var bookingId = (await accommodation.AddBookingAsync(ABooking(id, points[0], guests: [anna]), cancellationToken))!.Value;
            await accommodation.SetSelfAsync(id, points[1], anna, cancellationToken);

            (await references.AnyAsync(id, [points[0]], cancellationToken)).Should().BeTrue();
            (await references.AnyAsync(id, [points[1]], cancellationToken)).Should().BeTrue();
            (await references.AnyAsync(id, [points[2]], cancellationToken)).Should().BeFalse();
            (await references.AnyAsync(id, [], cancellationToken)).Should().BeFalse();

            await accommodation.CancelBookingAsync(id, bookingId, cancellationToken);
            (await references.AnyAsync(id, [points[0]], cancellationToken)).Should().BeTrue();
            (await references.AnyAsync(id + 1000, [points[0]], cancellationToken)).Should().BeFalse();
        }
        finally
        {
            await CleanUpAsync(id, anna);
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(anna);
        }
    }

    [Fact]
    public async Task Deleting_a_convoy_takes_its_bookings_and_flags_with_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, accommodation, _) = await ConnectOrSkipAsync(cancellationToken);
        var anna = await AddDriverAsync("Anna", "One");
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            var points = await RouteOfAsync(convoys, id, "Lille");
            await accommodation.AddBookingAsync(ABooking(id, points[0], guests: [anna]), cancellationToken);
            await accommodation.SetSelfAsync(id, points[0], anna, cancellationToken);

            (await convoys.DeleteAsync(id, cancellationToken)).Should().Be(DeleteResult.Deleted);

            (await ScalarAsync("SELECT COUNT(1) FROM dbo.AccommodationBooking WHERE ConvoyId = @id", ("@id", id))).Should().Be(0);
            (await ScalarAsync("SELECT COUNT(1) FROM dbo.SelfAccommodation WHERE ConvoyId = @id", ("@id", id))).Should().Be(0);
        }
        finally
        {
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(anna);
        }
    }

    [Fact]
    public async Task Erasing_a_guest_keeps_the_booking_and_leaves_no_name_on_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, accommodation, _) = await ConnectOrSkipAsync(cancellationToken);
        var anna = await AddDriverAsync("Anna", "One");
        var people = new UA.Action.Freedom.Data.People.PersonRepository(ConnectionFactory(), Unattributed);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            var points = await RouteOfAsync(convoys, id, "Lille");
            var bookingId = (await accommodation.AddBookingAsync(ABooking(id, points[0], guests: [anna]), cancellationToken))!.Value;

            (await people.DeleteAsync(anna, cancellationToken)).Should().Be(UA.Action.Freedom.Application.People.DeletePersonResult.Deleted);

            var booking = (await accommodation.GetBookingAsync(id, bookingId, cancellationToken))!;
            booking.Guests.Should().Equal(anna);
            (await ScalarAsync("SELECT COUNT(1) FROM dbo.PersonDetail WHERE PersonId = @id", ("@id", anna))).Should().Be(0);
            (await ScalarAsync("SELECT COUNT(1) FROM dbo.Person WHERE Id = @id AND ErasedAt IS NOT NULL", ("@id", anna))).Should().Be(1);
        }
        finally
        {
            await CleanUpAsync(id, anna);
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(anna);
        }
    }
}
