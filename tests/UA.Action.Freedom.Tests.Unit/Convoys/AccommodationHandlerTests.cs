using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// Accommodation (P2, P8, P13, P16, O4, O30): bookings and self-arranged nights belong to people crewed on the
/// convoy and to a route point of it; a leftover booking is reported, never blocked on.
/// </summary>
public class AccommodationHandlerTests
{
    private static readonly Guid Anna = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Boris = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Carla = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private const int Lille = 7;
    private const int Reims = 8;
    private const int Metz = 9;

    private sealed record Arrangement(
        IConvoyRepository Convoys,
        IConvoyVehicleRepository TruckList,
        IAccommodationRepository Accommodation)
    {
        public List<ConvoyVehicleReadModel> Vehicles { get; } = [];
    }

    private static VehicleCrewReadModel ACrewMember(Guid id, string first, string last, CrewRole role = CrewRole.Driver) =>
        new(id, first, last, role);

    private static RouteStopReadModel AStop(int id, string name, RoutePointKind kind) =>
        new(id, null, null, name, "France", "", "FR", id, name, kind);

    private static Arrangement AConvoyWithRoute(bool arrived = false)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns(arrived ? ConvoyTestData.AnArrivedConvoy() : ConvoyTestData.AReadModel());
        convoys.GetRouteAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(
        [
            AStop(Lille, "Lille", RoutePointKind.Overnight),
            AStop(Reims, "Reims", RoutePointKind.Overnight),
            AStop(Metz, "Metz", RoutePointKind.Stop),
        ]);

        var truckList = Substitute.For<IConvoyVehicleRepository>();
        var accommodation = Substitute.For<IAccommodationRepository>();
        accommodation.ListBookingsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
        accommodation.ListSelfAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
        var arrangement = new Arrangement(convoys, truckList, accommodation);
        truckList.ListAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<ConvoyVehicleReadModel>)[.. arrangement.Vehicles]);
        return arrangement;
    }

    private static Arrangement WithCrew(
        Arrangement arrangement, string vin, params VehicleCrewReadModel[] crew)
    {
        arrangement.Vehicles.Add(ConvoyTestData.AVehicle(vin));
        arrangement.TruckList.ListCrewAsync(ConvoyTestData.Id, vin, Arg.Any<CancellationToken>()).Returns(crew);
        return arrangement;
    }

    private static Arrangement WithWithdrawnVehicle(Arrangement arrangement, string vin, params VehicleCrewReadModel[] crew)
    {
        arrangement.Vehicles.Add(ConvoyTestData.AWithdrawnVehicle(vin));
        arrangement.TruckList.ListCrewAsync(ConvoyTestData.Id, vin, Arg.Any<CancellationToken>()).Returns(crew);
        return arrangement;
    }

    private static AccommodationBookingRecord ARecord(int routePointId = Lille, params Guid[] guests) => new(
        ConvoyTestData.Id, routePointId, "Ibis Lille", "REF-1",
        ConvoyTestData.Start, ConvoyTestData.Start.AddDays(1), null, 120m, guests);

    private static AccommodationBookingReadModel AStoredBooking(
        int id, int routePointId, bool cancelled = false, params Guid[] guests) => new(
        id, ConvoyTestData.Id, routePointId, "Ibis Lille", null, ConvoyTestData.Start, ConvoyTestData.Start.AddDays(1),
        null, null, cancelled, guests);

    private static readonly VehicleCrewReadModel AnnaCrew = ACrewMember(Anna, "Anna", "One");
    private static readonly VehicleCrewReadModel BorisCrew = ACrewMember(Boris, "Boris", "Two");

    private static BookAccommodationHandler BookHandler(Arrangement a) =>
        new(a.Convoys, a.TruckList, a.Accommodation);

    [Fact]
    public async Task Books_a_stay_for_people_crewed_on_the_convoy()
    {
        var a = WithCrew(AConvoyWithRoute(), "VIN-1", AnnaCrew, BorisCrew);
        a.Accommodation.AddBookingAsync(Arg.Any<AccommodationBookingRecord>(), Arg.Any<CancellationToken>()).Returns(55);

        var result = await BookHandler(a).HandleAsync(
            new BookAccommodationCommand(ARecord(Lille, Anna, Boris)), CancellationToken.None);

        result.Should().Be(new BookAccommodationResult(BookAccommodationOutcome.Booked, 55));
    }

    [Fact]
    public async Task Refuses_a_guest_who_is_not_crewed_and_writes_nothing()
    {
        var a = WithCrew(AConvoyWithRoute(), "VIN-1", AnnaCrew);

        var result = await BookHandler(a).HandleAsync(
            new BookAccommodationCommand(ARecord(Lille, Anna, Carla)), CancellationToken.None);

        result.Outcome.Should().Be(BookAccommodationOutcome.GuestNotOnCrew);
        await a.Accommodation.DidNotReceive().AddBookingAsync(Arg.Any<AccommodationBookingRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_person_on_a_withdrawn_vehicle_is_no_longer_crew_of_the_convoy()
    {
        var a = WithWithdrawnVehicle(AConvoyWithRoute(), "VIN-9", ACrewMember(Carla, "Carla", "Three"));

        var result = await BookHandler(a).HandleAsync(
            new BookAccommodationCommand(ARecord(Lille, Carla)), CancellationToken.None);

        result.Outcome.Should().Be(BookAccommodationOutcome.GuestNotOnCrew);
    }

    [Fact]
    public async Task Reports_a_route_point_that_is_not_on_the_convoy()
    {
        var a = WithCrew(AConvoyWithRoute(), "VIN-1", AnnaCrew);
        a.Accommodation.AddBookingAsync(Arg.Any<AccommodationBookingRecord>(), Arg.Any<CancellationToken>()).Returns((int?)null);

        var result = await BookHandler(a).HandleAsync(
            new BookAccommodationCommand(ARecord(999, Anna)), CancellationToken.None);

        result.Outcome.Should().Be(BookAccommodationOutcome.PointNotOnConvoy);
    }

    [Fact]
    public async Task Books_nothing_for_an_unknown_or_arrived_convoy()
    {
        var unknown = new Arrangement(
            Substitute.For<IConvoyRepository>(),
            Substitute.For<IConvoyVehicleRepository>(),
            Substitute.For<IAccommodationRepository>());
        var arrived = AConvoyWithRoute(arrived: true);

        (await BookHandler(unknown).HandleAsync(new BookAccommodationCommand(ARecord()), CancellationToken.None))
            .Outcome.Should().Be(BookAccommodationOutcome.ConvoyNotFound);
        (await BookHandler(arrived).HandleAsync(new BookAccommodationCommand(ARecord()), CancellationToken.None))
            .Outcome.Should().Be(BookAccommodationOutcome.ConvoyArrived);
    }

    [Fact]
    public async Task Replacing_may_keep_a_guest_who_has_left_but_not_add_one_who_is_not_crewed()
    {
        var a = WithCrew(AConvoyWithRoute(), "VIN-1", AnnaCrew);
        a.Accommodation.GetBookingAsync(ConvoyTestData.Id, 5, Arg.Any<CancellationToken>())
            .Returns(AStoredBooking(5, Lille, false, Boris));
        a.Accommodation.ReplaceBookingAsync(5, Arg.Any<AccommodationBookingRecord>(), Arg.Any<CancellationToken>())
            .Returns(ReplaceBookingResult.Replaced);
        var handler = new ReplaceAccommodationHandler(a.Convoys, a.TruckList, a.Accommodation);

        (await handler.HandleAsync(new ReplaceAccommodationCommand(5, ARecord(Lille, Anna, Boris)), CancellationToken.None))
            .Should().Be(ReplaceAccommodationOutcome.Replaced);
        (await handler.HandleAsync(new ReplaceAccommodationCommand(5, ARecord(Lille, Carla)), CancellationToken.None))
            .Should().Be(ReplaceAccommodationOutcome.GuestNotOnCrew);
    }

    [Fact]
    public async Task Replacing_reports_a_missing_or_cancelled_booking()
    {
        var a = WithCrew(AConvoyWithRoute(), "VIN-1", AnnaCrew);
        a.Accommodation.GetBookingAsync(ConvoyTestData.Id, 5, Arg.Any<CancellationToken>()).Returns(AStoredBooking(5, Lille, true, Anna));
        var handler = new ReplaceAccommodationHandler(a.Convoys, a.TruckList, a.Accommodation);

        (await handler.HandleAsync(new ReplaceAccommodationCommand(6, ARecord(Lille, Anna)), CancellationToken.None))
            .Should().Be(ReplaceAccommodationOutcome.NotFound);
        (await handler.HandleAsync(new ReplaceAccommodationCommand(5, ARecord(Lille, Anna)), CancellationToken.None))
            .Should().Be(ReplaceAccommodationOutcome.Cancelled);
    }

    [Fact]
    public async Task Cancelling_a_booking_reports_whether_it_was_there()
    {
        var a = AConvoyWithRoute();
        a.Accommodation.CancelBookingAsync(ConvoyTestData.Id, 5, Arg.Any<CancellationToken>()).Returns(true);
        a.Accommodation.CancelBookingAsync(ConvoyTestData.Id, 6, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new CancelAccommodationHandler(a.Convoys, a.Accommodation);

        (await handler.HandleAsync(new CancelAccommodationCommand(ConvoyTestData.Id, 5), CancellationToken.None))
            .Should().Be(CancelAccommodationOutcome.Cancelled);
        (await handler.HandleAsync(new CancelAccommodationCommand(ConvoyTestData.Id, 6), CancellationToken.None))
            .Should().Be(CancelAccommodationOutcome.NotFound);
    }

    [Fact]
    public async Task Migrating_hands_a_leavers_place_to_a_replacement_who_is_crewed()
    {
        var a = WithCrew(AConvoyWithRoute(), "VIN-1", ACrewMember(Carla, "Carla", "Three"));
        a.Accommodation.MigrateGuestAsync(ConvoyTestData.Id, 5, Anna, Carla, Arg.Any<CancellationToken>())
            .Returns(MigrateGuestResult.Migrated);

        var outcome = await new MigrateAccommodationHandler(a.Convoys, a.TruckList, a.Accommodation).HandleAsync(
            new MigrateAccommodationCommand(ConvoyTestData.Id, 5, Anna, Carla), CancellationToken.None);

        outcome.Should().Be(MigrateAccommodationOutcome.Migrated);
    }

    [Fact]
    public async Task Migrating_to_someone_who_is_not_crewed_changes_nothing()
    {
        var a = WithCrew(AConvoyWithRoute(), "VIN-1", AnnaCrew);

        var outcome = await new MigrateAccommodationHandler(a.Convoys, a.TruckList, a.Accommodation).HandleAsync(
            new MigrateAccommodationCommand(ConvoyTestData.Id, 5, Boris, Carla), CancellationToken.None);

        outcome.Should().Be(MigrateAccommodationOutcome.ReplacementNotOnCrew);
        await a.Accommodation.DidNotReceive().MigrateGuestAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(MigrateGuestResult.NotFound, MigrateAccommodationOutcome.NotFound)]
    [InlineData(MigrateGuestResult.Cancelled, MigrateAccommodationOutcome.Cancelled)]
    [InlineData(MigrateGuestResult.NotAGuest, MigrateAccommodationOutcome.NotAGuest)]
    public async Task Migrating_reports_what_the_store_found(MigrateGuestResult found, MigrateAccommodationOutcome expected)
    {
        var a = WithCrew(AConvoyWithRoute(), "VIN-1", ACrewMember(Carla, "Carla", "Three"));
        a.Accommodation.MigrateGuestAsync(ConvoyTestData.Id, 5, Anna, Carla, Arg.Any<CancellationToken>()).Returns(found);

        var outcome = await new MigrateAccommodationHandler(a.Convoys, a.TruckList, a.Accommodation).HandleAsync(
            new MigrateAccommodationCommand(ConvoyTestData.Id, 5, Anna, Carla), CancellationToken.None);

        outcome.Should().Be(expected);
    }

    [Fact]
    public async Task Flags_a_crew_member_as_arranging_their_own_stay_at_an_overnight_stop()
    {
        var a = WithCrew(AConvoyWithRoute(), "VIN-1", AnnaCrew);
        a.Accommodation.SetSelfAsync(ConvoyTestData.Id, Reims, Anna, Arg.Any<CancellationToken>()).Returns(true);

        var outcome = await new SetSelfAccommodationHandler(a.Convoys, a.TruckList, a.Accommodation).HandleAsync(
            new SetSelfAccommodationCommand(ConvoyTestData.Id, Reims, Anna), CancellationToken.None);

        outcome.Should().Be(SetSelfAccommodationOutcome.Set);
    }

    [Fact]
    public async Task A_stop_that_is_not_overnight_cannot_be_flagged()
    {
        var a = WithCrew(AConvoyWithRoute(), "VIN-1", AnnaCrew);

        var outcome = await new SetSelfAccommodationHandler(a.Convoys, a.TruckList, a.Accommodation).HandleAsync(
            new SetSelfAccommodationCommand(ConvoyTestData.Id, Metz, Anna), CancellationToken.None);

        outcome.Should().Be(SetSelfAccommodationOutcome.NotOvernight);
        await a.Accommodation.DidNotReceive().SetSelfAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_flag_needs_a_crew_member_and_a_stop_of_the_convoy()
    {
        var a = WithCrew(AConvoyWithRoute(), "VIN-1", AnnaCrew);
        var handler = new SetSelfAccommodationHandler(a.Convoys, a.TruckList, a.Accommodation);

        (await handler.HandleAsync(new SetSelfAccommodationCommand(ConvoyTestData.Id, Lille, Carla), CancellationToken.None))
            .Should().Be(SetSelfAccommodationOutcome.PersonNotOnCrew);
        (await handler.HandleAsync(new SetSelfAccommodationCommand(ConvoyTestData.Id, 999, Anna), CancellationToken.None))
            .Should().Be(SetSelfAccommodationOutcome.PointNotOnConvoy);
    }

    [Fact]
    public async Task Clearing_a_flag_reports_whether_it_was_set()
    {
        var a = AConvoyWithRoute();
        a.Accommodation.RemoveSelfAsync(ConvoyTestData.Id, Lille, Anna, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new ClearSelfAccommodationHandler(a.Convoys, a.Accommodation);

        (await handler.HandleAsync(new ClearSelfAccommodationCommand(ConvoyTestData.Id, Lille, Anna), CancellationToken.None))
            .Should().Be(ClearSelfAccommodationOutcome.Cleared);
        (await handler.HandleAsync(new ClearSelfAccommodationCommand(ConvoyTestData.Id, Reims, Anna), CancellationToken.None))
            .Should().Be(ClearSelfAccommodationOutcome.NotFound);
    }

    [Fact]
    public async Task Coverage_shows_the_grid_with_the_one_missing_cell_and_no_warnings_when_nothing_is_left_over()
    {
        var a = WithCrew(AConvoyWithRoute(), "VIN-1", AnnaCrew, BorisCrew);
        a.Accommodation.ListBookingsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns([AStoredBooking(1, Lille, false, Anna, Boris)]);
        a.Accommodation.ListSelfAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns([new SelfAccommodationReadModel(ConvoyTestData.Id, Reims, Anna)]);

        var coverage = await new GetAccommodationCoverageHandler(a.Convoys, a.TruckList, a.Accommodation).HandleAsync(
            new GetAccommodationCoverageQuery(ConvoyTestData.Id), CancellationToken.None);

        coverage!.Cells.Select(cell => (cell.RoutePointId, cell.PersonId, cell.Status)).Should().BeEquivalentTo(
        [
            (Lille, Anna, CoverageStatus.Booked),
            (Lille, Boris, CoverageStatus.Booked),
            (Reims, Anna, CoverageStatus.SelfArranged),
            (Reims, Boris, CoverageStatus.Missing),
        ]);
        coverage.Stops.Select(stop => stop.RoutePointId).Should().Equal(Lille, Reims);
        coverage.Crew.Select(person => person.Name).Should().Equal("Anna One", "Boris Two");
        coverage.MissingCount.Should().Be(1);
        coverage.AllCovered.Should().BeFalse();
        coverage.LeftoverBookings.Should().BeEmpty();
        coverage.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task A_booking_of_someone_who_left_the_crew_is_a_warning_naming_the_stop()
    {
        var a = WithCrew(AConvoyWithRoute(), "VIN-1", BorisCrew);
        a.Accommodation.ListBookingsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns([AStoredBooking(1, Lille, false, Anna), AStoredBooking(2, Reims, true, Anna)]);

        var coverage = await new GetAccommodationCoverageHandler(a.Convoys, a.TruckList, a.Accommodation).HandleAsync(
            new GetAccommodationCoverageQuery(ConvoyTestData.Id), CancellationToken.None);

        coverage!.LeftoverBookings.Should().ContainSingle().Which.Should().Match<LeftoverBookingReadModel>(
            leftover => leftover.BookingId == 1 && leftover.RoutePointId == Lille);
        coverage.Warnings.Should().ContainSingle().Which.Should().Contain("Lille").And.Contain("cancel or migrate");
    }

    [Fact]
    public async Task Coverage_of_an_unknown_convoy_is_not_found()
    {
        var unknown = new Arrangement(
            Substitute.For<IConvoyRepository>(),
            Substitute.For<IConvoyVehicleRepository>(),
            Substitute.For<IAccommodationRepository>());

        var coverage = await new GetAccommodationCoverageHandler(unknown.Convoys, unknown.TruckList, unknown.Accommodation)
            .HandleAsync(new GetAccommodationCoverageQuery(ConvoyTestData.Id), CancellationToken.None);

        coverage.Should().BeNull();
    }

    [Fact]
    public async Task A_leftover_booking_is_a_dispatcher_task_beside_the_redeclare_tasks()
    {
        var a = WithCrew(AConvoyWithRoute(), "VIN-1", BorisCrew);
        a.Accommodation.ListBookingsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns([AStoredBooking(1, Lille, false, Anna)]);
        var redeclare = Substitute.For<UA.Action.Freedom.Application.Abstractions.IQueryHandler<
            UA.Action.Freedom.Application.Declarations.ListRedeclareTasksQuery,
            IReadOnlyList<UA.Action.Freedom.Application.Declarations.RedeclareTaskReadModel>?>>();
        redeclare.HandleAsync(Arg.Any<UA.Action.Freedom.Application.Declarations.ListRedeclareTasksQuery>(), Arg.Any<CancellationToken>())
            .Returns([new UA.Action.Freedom.Application.Declarations.RedeclareTaskReadModel(
                3, "VIN-1", DeclarationKind.Gmr, null, null,
                UA.Action.Freedom.Application.Declarations.RedeclareResolution.UpdateOrRecreate)]);

        var tasks = await new ListConvoyTasksHandler(redeclare, a.Convoys, a.TruckList, a.Accommodation).HandleAsync(
            new ListConvoyTasksQuery(ConvoyTestData.Id), CancellationToken.None);

        tasks.Should().HaveCount(2);
        tasks!.OfType<UA.Action.Freedom.Application.Declarations.RedeclareTaskReadModel>().Should().ContainSingle();
        tasks.OfType<LeftoverBookingTaskReadModel>().Should().ContainSingle().Which.Should().Match<LeftoverBookingTaskReadModel>(
            task => task.BookingId == 1 && task.RoutePointId == Lille && task.Resolution == LeftoverBookingResolution.CancelOrMigrate);
    }

    [Fact]
    public async Task Tasks_of_an_unknown_convoy_are_not_found()
    {
        var a = new Arrangement(
            Substitute.For<IConvoyRepository>(),
            Substitute.For<IConvoyVehicleRepository>(),
            Substitute.For<IAccommodationRepository>());
        var redeclare = Substitute.For<UA.Action.Freedom.Application.Abstractions.IQueryHandler<
            UA.Action.Freedom.Application.Declarations.ListRedeclareTasksQuery,
            IReadOnlyList<UA.Action.Freedom.Application.Declarations.RedeclareTaskReadModel>?>>();
        redeclare.HandleAsync(Arg.Any<UA.Action.Freedom.Application.Declarations.ListRedeclareTasksQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<UA.Action.Freedom.Application.Declarations.RedeclareTaskReadModel>?)null);

        var tasks = await new ListConvoyTasksHandler(redeclare, a.Convoys, a.TruckList, a.Accommodation).HandleAsync(
            new ListConvoyTasksQuery(ConvoyTestData.Id), CancellationToken.None);

        tasks.Should().BeNull();
    }
}
