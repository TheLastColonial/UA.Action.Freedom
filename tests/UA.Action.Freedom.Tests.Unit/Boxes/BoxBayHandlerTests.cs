using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Unit.Boxes;

/// <summary>
/// Bay allocation: a Loader placing a box in a 1m by 1m storage bay so it can be found again.
/// </summary>
/// <remarks>
/// A box may only be in one bay, within one location, at a time. That is enforced here by
/// refusing to assign a bay that does not belong to the box's current location — not by a
/// second lookup the repository has to keep in step (docs/domain/key-concepts.md § Box).
/// </remarks>
public class BoxBayHandlerTests
{
    private const int BoxId = 7;
    private const int BayId = 9;
    private const int LocationId = 3;
    private const int OtherLocationId = 4;

    private static readonly Guid Loader = new("2b9c1e40-7d8a-4c31-9f52-6a0b8d3e5c11");

    private static BoxReadModel ABox(int? locationId = LocationId) => new(
        BoxId, WeightKg: 0, WidthCm: null, DepthCm: null, HeightCm: null,
        ReceiverRef: null, LocationId: locationId, ValidatedByPersonId: null, ValidatedAt: null);

    private static BayReadModel ABay(int locationId = LocationId) => new(BayId, locationId, "A1");

    private static IPersonRepository AKnownLoader()
    {
        var people = Substitute.For<IPersonRepository>();
        people.ExistsAsync(Loader, Arg.Any<CancellationToken>()).Returns(true);
        return people;
    }

    [Fact]
    public async Task A_loader_places_a_box_in_a_bay_at_its_own_location()
    {
        var boxes = Substitute.For<IBoxRepository>();
        var bays = Substitute.For<IBayRepository>();
        boxes.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        bays.GetByIdAsync(BayId, Arg.Any<CancellationToken>()).Returns(ABay());
        var handler = new AssignBoxBayHandler(boxes, bays, AKnownLoader());

        var outcome = await handler.HandleAsync(
            new AssignBoxBayCommand(BoxId, BayId, Loader), CancellationToken.None);

        outcome.Should().Be(AssignBoxBayOutcome.Assigned);
        await boxes.Received(1).AssignBayAsync(
            BoxId, BayId, Loader, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_box_cannot_be_placed_in_a_bay_belonging_to_a_different_location()
    {
        var boxes = Substitute.For<IBoxRepository>();
        var bays = Substitute.For<IBayRepository>();
        boxes.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox(locationId: LocationId));
        bays.GetByIdAsync(BayId, Arg.Any<CancellationToken>()).Returns(ABay(locationId: OtherLocationId));
        var handler = new AssignBoxBayHandler(boxes, bays, AKnownLoader());

        var outcome = await handler.HandleAsync(
            new AssignBoxBayCommand(BoxId, BayId, Loader), CancellationToken.None);

        outcome.Should().Be(AssignBoxBayOutcome.LocationMismatch);
        await boxes.DidNotReceive().AssignBayAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_box_with_no_location_yet_cannot_be_placed_in_any_bay()
    {
        // Checked in at a depot is a precondition for shelving it there.
        var boxes = Substitute.For<IBoxRepository>();
        var bays = Substitute.For<IBayRepository>();
        boxes.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox(locationId: null));
        bays.GetByIdAsync(BayId, Arg.Any<CancellationToken>()).Returns(ABay());
        var handler = new AssignBoxBayHandler(boxes, bays, AKnownLoader());

        var outcome = await handler.HandleAsync(
            new AssignBoxBayCommand(BoxId, BayId, Loader), CancellationToken.None);

        outcome.Should().Be(AssignBoxBayOutcome.LocationMismatch);
    }

    [Fact]
    public async Task Placing_an_unknown_box_reports_not_found()
    {
        var boxes = Substitute.For<IBoxRepository>();
        var bays = Substitute.For<IBayRepository>();
        boxes.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns((BoxReadModel?)null);
        var handler = new AssignBoxBayHandler(boxes, bays, AKnownLoader());

        var outcome = await handler.HandleAsync(
            new AssignBoxBayCommand(BoxId, BayId, Loader), CancellationToken.None);

        outcome.Should().Be(AssignBoxBayOutcome.BoxNotFound);
    }

    [Fact]
    public async Task Placing_a_box_in_an_unknown_bay_reports_not_found()
    {
        var boxes = Substitute.For<IBoxRepository>();
        var bays = Substitute.For<IBayRepository>();
        boxes.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        bays.GetByIdAsync(BayId, Arg.Any<CancellationToken>()).Returns((BayReadModel?)null);
        var handler = new AssignBoxBayHandler(boxes, bays, AKnownLoader());

        var outcome = await handler.HandleAsync(
            new AssignBoxBayCommand(BoxId, BayId, Loader), CancellationToken.None);

        outcome.Should().Be(AssignBoxBayOutcome.BayNotFound);
    }

    [Fact]
    public async Task Naming_an_assigner_who_is_not_a_volunteer_on_file_is_refused()
    {
        var boxes = Substitute.For<IBoxRepository>();
        var bays = Substitute.For<IBayRepository>();
        var people = Substitute.For<IPersonRepository>();
        boxes.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        bays.GetByIdAsync(BayId, Arg.Any<CancellationToken>()).Returns(ABay());
        people.ExistsAsync(Loader, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new AssignBoxBayHandler(boxes, bays, people);

        var outcome = await handler.HandleAsync(
            new AssignBoxBayCommand(BoxId, BayId, Loader), CancellationToken.None);

        outcome.Should().Be(AssignBoxBayOutcome.NoSuchAssigner);
        await boxes.DidNotReceive().AssignBayAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Vacating_a_box_that_is_in_a_bay_reports_it_vacated()
    {
        var boxes = Substitute.For<IBoxRepository>();
        boxes.VacateActiveBayAssignmentAsync(BoxId, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new VacateBoxBayHandler(boxes);

        var outcome = await handler.HandleAsync(new VacateBoxBayCommand(BoxId), CancellationToken.None);

        outcome.Should().Be(VacateBoxBayOutcome.Vacated);
    }

    [Fact]
    public async Task Vacating_a_box_that_is_not_in_a_bay_reports_no_active_assignment()
    {
        var boxes = Substitute.For<IBoxRepository>();
        boxes.VacateActiveBayAssignmentAsync(BoxId, Arg.Any<CancellationToken>()).Returns(false);
        boxes.ExistsAsync(BoxId, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new VacateBoxBayHandler(boxes);

        var outcome = await handler.HandleAsync(new VacateBoxBayCommand(BoxId), CancellationToken.None);

        outcome.Should().Be(VacateBoxBayOutcome.NoActiveAssignment);
    }

    [Fact]
    public async Task Vacating_an_unknown_box_reports_not_found()
    {
        var boxes = Substitute.For<IBoxRepository>();
        boxes.VacateActiveBayAssignmentAsync(BoxId, Arg.Any<CancellationToken>()).Returns(false);
        boxes.ExistsAsync(BoxId, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new VacateBoxBayHandler(boxes);

        var outcome = await handler.HandleAsync(new VacateBoxBayCommand(BoxId), CancellationToken.None);

        outcome.Should().Be(VacateBoxBayOutcome.BoxNotFound);
    }
}
