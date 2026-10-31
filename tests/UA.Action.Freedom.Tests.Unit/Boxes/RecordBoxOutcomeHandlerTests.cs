using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Domain;
using UA.Action.Freedom.Tests.Unit.Convoys;

namespace UA.Action.Freedom.Tests.Unit.Boxes;

/// <summary>Recording how a box ended on a convoy: the checks that come before the write.</summary>
public class RecordBoxOutcomeHandlerTests
{
    private const int ConvoyId = ConvoyTestData.Id;
    private const int BoxId = 7;
    private const int HubId = 3;
    private const string Vin = "WVWZZZ1JZXW000001";

    private static ConvoyReadModel ADepartedConvoy() =>
        ConvoyTestData.APublishedConvoy() with { DepartedAt = ConvoyTestData.Start };

    private static BoxOutcomeReadModel AnOutcome(BoxOutcome outcome) =>
        new(BoxId, ConvoyId, Vin, outcome, null, null, ConvoyTestData.Start);

    private sealed record World(
        IConvoyRepository Convoys,
        IConvoyVehicleRepository TruckList,
        ILocationRepository Locations,
        IBoxOutcomeRepository Outcomes)
    {
        public Task<RecordBoxOutcomeOutcome> Record(
            BoxOutcome outcome, string? reason = null, int? hubLocationId = null) =>
            new RecordBoxOutcomeHandler(Convoys, TruckList, Locations, Outcomes).HandleAsync(
                new RecordBoxOutcomeCommand(ConvoyId, BoxId, outcome, reason, hubLocationId),
                TestContext.Current.CancellationToken);
    }

    private static World AWorld(
        ConvoyReadModel? convoy = null,
        bool boxOnConvoy = true,
        BoxOutcomeReadModel? current = null,
        bool hubRegistered = true,
        RecordBoxOutcomeResult write = RecordBoxOutcomeResult.Recorded)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyId, Arg.Any<CancellationToken>()).Returns(convoy ?? ADepartedConvoy());

        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.GetBoxAllocationAsync(BoxId, Arg.Any<CancellationToken>()).Returns(
            boxOnConvoy ? new BoxAllocation(new ConvoyId(ConvoyId), Vin, BoxId, ConvoyTestData.Start) : null);

        var locations = Substitute.For<ILocationRepository>();
        locations.GetByIdAsync(HubId, Arg.Any<CancellationToken>()).Returns(
            new LocationReadModel(HubId, "Hub", null, null, null, null, null, hubRegistered));

        var outcomes = Substitute.For<IBoxOutcomeRepository>();
        outcomes.GetCurrentAsync(ConvoyId, BoxId, Arg.Any<CancellationToken>()).Returns(current);
        outcomes.RecordAsync(Arg.Any<RecordBoxOutcome>(), Arg.Any<CancellationToken>()).Returns(write);

        return new World(convoys, truckList, locations, outcomes);
    }

    [Fact]
    public async Task Records_a_delivered_box_on_a_departed_convoy()
    {
        var world = AWorld();

        (await world.Record(BoxOutcome.Delivered)).Should().Be(RecordBoxOutcomeOutcome.Recorded);

        await world.Outcomes.Received(1).RecordAsync(
            new RecordBoxOutcome(ConvoyId, Vin, BoxId, null, BoxOutcome.Delivered, null, null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Passes_the_current_outcome_so_the_write_can_settle_a_race()
    {
        var world = AWorld(current: AnOutcome(BoxOutcome.Delivered));

        await world.Record(BoxOutcome.Accepted);

        await world.Outcomes.Received(1).RecordAsync(
            Arg.Is<RecordBoxOutcome>(write => write.From == BoxOutcome.Delivered),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_an_unknown_convoy()
    {
        var world = AWorld();
        world.Convoys.GetByIdAsync(ConvoyId, Arg.Any<CancellationToken>()).Returns((ConvoyReadModel?)null);

        (await world.Record(BoxOutcome.Delivered)).Should().Be(RecordBoxOutcomeOutcome.ConvoyNotFound);
    }

    [Fact]
    public async Task Refuses_a_convoy_that_has_not_departed()
    {
        var world = AWorld(convoy: ConvoyTestData.APublishedConvoy());

        (await world.Record(BoxOutcome.Delivered)).Should().Be(RecordBoxOutcomeOutcome.ConvoyNotDeparted);
    }

    [Fact]
    public async Task Refuses_a_box_that_is_not_on_a_vehicle_of_the_convoy()
    {
        var world = AWorld(boxOnConvoy: false);

        (await world.Record(BoxOutcome.Delivered)).Should().Be(RecordBoxOutcomeOutcome.BoxNotOnConvoy);
    }

    [Fact]
    public async Task A_box_returned_to_a_hub_has_no_allocation_so_a_second_outcome_is_illegal_not_missing()
    {
        var world = AWorld(boxOnConvoy: false, current: AnOutcome(BoxOutcome.ReturnedToHub));

        (await world.Record(BoxOutcome.Seized)).Should().Be(RecordBoxOutcomeOutcome.IllegalTransition);
    }

    [Fact]
    public async Task Refuses_an_edge_the_lifecycle_does_not_have()
    {
        var world = AWorld(current: AnOutcome(BoxOutcome.Seized));

        (await world.Record(BoxOutcome.Delivered)).Should().Be(RecordBoxOutcomeOutcome.IllegalTransition);
        await world.Outcomes.DidNotReceive().RecordAsync(Arg.Any<RecordBoxOutcome>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_an_undeliverable_box_without_a_bounded_reason()
    {
        var world = AWorld();

        (await world.Record(BoxOutcome.Undeliverable, "free text")).Should().Be(RecordBoxOutcomeOutcome.ReasonRequired);
    }

    [Fact]
    public async Task Records_an_undeliverable_box_with_its_reason()
    {
        var world = AWorld();

        (await world.Record(BoxOutcome.Undeliverable, "stolen")).Should().Be(RecordBoxOutcomeOutcome.Recorded);
    }

    [Fact]
    public async Task Refuses_a_return_to_a_hub_that_is_not_registered()
    {
        var world = AWorld(hubRegistered: false);

        (await world.Record(BoxOutcome.ReturnedToHub, hubLocationId: HubId))
            .Should().Be(RecordBoxOutcomeOutcome.HubNotRegistered);
    }

    [Fact]
    public async Task Refuses_a_return_that_names_no_hub()
    {
        var world = AWorld();

        (await world.Record(BoxOutcome.ReturnedToHub)).Should().Be(RecordBoxOutcomeOutcome.HubNotRegistered);
    }

    [Fact]
    public async Task Refuses_a_return_to_a_location_that_does_not_exist()
    {
        var world = AWorld();
        world.Locations.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((LocationReadModel?)null);

        (await world.Record(BoxOutcome.ReturnedToHub, hubLocationId: 99))
            .Should().Be(RecordBoxOutcomeOutcome.HubNotRegistered);
    }

    [Fact]
    public async Task Records_a_return_to_a_registered_hub()
    {
        var world = AWorld();

        (await world.Record(BoxOutcome.ReturnedToHub, hubLocationId: HubId)).Should().Be(RecordBoxOutcomeOutcome.Recorded);
    }

    [Fact]
    public async Task Reports_a_conflict_when_another_writer_got_there_first()
    {
        var world = AWorld(write: RecordBoxOutcomeResult.Conflict);

        (await world.Record(BoxOutcome.Delivered)).Should().Be(RecordBoxOutcomeOutcome.Conflict);
    }
}
