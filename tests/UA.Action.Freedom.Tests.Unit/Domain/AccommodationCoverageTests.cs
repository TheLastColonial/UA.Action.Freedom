using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>
/// Every crew member must be covered at every overnight stop, by a booking or by arranging their own (P8, O4, O30).
/// A booking that covers nobody who is still crewed is left over, and that is a warning, not a block (P13, P16).
/// </summary>
public class AccommodationCoverageTests
{
    private static readonly Guid Anna = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Boris = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static RoutePoint APoint(int id, RoutePointKind kind = RoutePointKind.Overnight) =>
        new(id, id, $"Point {id}", kind, new Address());

    private static AccommodationBooking ABooking(int id, int routePointId, params Guid[] guests) =>
        new(id, routePointId, guests);

    private static CoverageStatus StatusOf(IReadOnlyList<CoverageCell> grid, int routePointId, Guid person) =>
        grid.Single(cell => cell.RoutePointId == routePointId && cell.PersonId == person).Status;

    [Fact]
    public void A_booking_at_the_stop_covers_its_guest()
    {
        var grid = Accommodation.Coverage([APoint(1)], [Anna], [ABooking(10, 1, Anna)], []);

        StatusOf(grid, 1, Anna).Should().Be(CoverageStatus.Booked);
    }

    [Fact]
    public void One_shared_booking_covers_everyone_named_on_it()
    {
        var grid = Accommodation.Coverage([APoint(1)], [Anna, Boris], [ABooking(10, 1, Anna, Boris)], []);

        grid.Should().OnlyContain(cell => cell.Status == CoverageStatus.Booked);
    }

    [Fact]
    public void Arranging_your_own_covers_that_person_at_that_stop_only()
    {
        var grid = Accommodation.Coverage(
            [APoint(1), APoint(2)], [Anna], [], [new SelfArrangement(1, Anna)]);

        StatusOf(grid, 1, Anna).Should().Be(CoverageStatus.SelfArranged);
        StatusOf(grid, 2, Anna).Should().Be(CoverageStatus.Missing);
    }

    [Fact]
    public void A_crew_member_with_no_booking_and_no_flag_is_missing()
    {
        var grid = Accommodation.Coverage([APoint(1)], [Anna, Boris], [ABooking(10, 1, Anna)], []);

        StatusOf(grid, 1, Anna).Should().Be(CoverageStatus.Booked);
        StatusOf(grid, 1, Boris).Should().Be(CoverageStatus.Missing);
    }

    [Fact]
    public void A_booking_at_one_stop_does_not_cover_another()
    {
        var grid = Accommodation.Coverage([APoint(1), APoint(2)], [Anna], [ABooking(10, 1, Anna)], []);

        StatusOf(grid, 2, Anna).Should().Be(CoverageStatus.Missing);
    }

    [Fact]
    public void A_stop_that_is_not_overnight_needs_nothing()
    {
        var grid = Accommodation.Coverage(
            [APoint(1, RoutePointKind.Stop), APoint(2, RoutePointKind.Border), APoint(3, RoutePointKind.Hub)],
            [Anna],
            [],
            []);

        grid.Should().BeEmpty();
    }

    [Fact]
    public void A_cancelled_booking_covers_nobody()
    {
        var cancelled = ABooking(10, 1, Anna) with { Cancelled = true };

        var grid = Accommodation.Coverage([APoint(1)], [Anna], [cancelled], []);

        StatusOf(grid, 1, Anna).Should().Be(CoverageStatus.Missing);
    }

    [Fact]
    public void A_booking_beats_a_flag_when_a_person_has_both()
    {
        var grid = Accommodation.Coverage(
            [APoint(1)], [Anna], [ABooking(10, 1, Anna)], [new SelfArrangement(1, Anna)]);

        StatusOf(grid, 1, Anna).Should().Be(CoverageStatus.Booked);
    }

    [Fact]
    public void The_grid_has_a_cell_for_every_overnight_stop_and_every_crew_member_in_route_order()
    {
        var grid = Accommodation.Coverage([APoint(2), APoint(1)], [Anna, Boris], [], []);

        grid.Select(cell => (cell.RoutePointId, cell.PersonId)).Should().Equal(
            (2, Anna), (2, Boris), (1, Anna), (1, Boris));
    }

    [Fact]
    public void A_booking_of_someone_no_longer_crewed_is_left_over()
    {
        var leftover = Accommodation.LeftoverBookings([ABooking(10, 1, Anna)], [Boris]);

        leftover.Select(booking => booking.Id).Should().Equal(10);
    }

    [Fact]
    public void A_shared_booking_is_not_left_over_while_one_guest_is_still_crewed()
    {
        var leftover = Accommodation.LeftoverBookings([ABooking(10, 1, Anna, Boris)], [Boris]);

        leftover.Should().BeEmpty();
    }

    [Fact]
    public void A_booking_with_no_guests_is_left_over()
    {
        var leftover = Accommodation.LeftoverBookings([ABooking(10, 1)], [Anna]);

        leftover.Should().ContainSingle();
    }

    [Fact]
    public void A_cancelled_booking_is_not_left_over_because_it_has_been_dealt_with()
    {
        var cancelled = ABooking(10, 1, Anna) with { Cancelled = true };

        Accommodation.LeftoverBookings([cancelled], []).Should().BeEmpty();
    }
}
