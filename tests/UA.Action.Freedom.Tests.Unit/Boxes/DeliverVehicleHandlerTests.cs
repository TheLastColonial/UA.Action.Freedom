using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;
using UA.Action.Freedom.Tests.Unit.Convoys;

namespace UA.Action.Freedom.Tests.Unit.Boxes;

/// <summary>
/// Marking a vehicle delivered (O8): its boxes are delivered with it, and for now its manifest moves to Delivered so the
/// arrival and erasure rules, which still read the manifest, keep working until plan 15.
/// </summary>
public class DeliverVehicleHandlerTests
{
    private const int ConvoyId = ConvoyTestData.Id;
    private const string Vin = "WVWZZZ1JZXW000001";

    private static ConvoyReadModel ADepartedConvoy() =>
        ConvoyTestData.APublishedConvoy() with { DepartedAt = ConvoyTestData.Start };

    private static ManifestReadModel AManifest(ManifestStatus status) =>
        new("MAN-1", ConvoyId, Vin, status, null, ConvoyTestData.Start);

    private sealed record World(
        DeliverVehicleHandler Handler, IBoxOutcomeRepository Outcomes, IManifestRepository Manifests)
    {
        public Task<DeliverVehicleResult> Deliver() =>
            Handler.HandleAsync(new DeliverVehicleCommand(ConvoyId, Vin), TestContext.Current.CancellationToken);
    }

    private static World AWorld(
        ConvoyReadModel? convoy = null,
        ConvoyVehicleReadModel? entry = null,
        ManifestReadModel? manifest = null,
        int? boxesDelivered = 2)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyId, Arg.Any<CancellationToken>()).Returns(convoy);

        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.GetAsync(ConvoyId, Vin, Arg.Any<CancellationToken>()).Returns(entry);

        var outcomes = Substitute.For<IBoxOutcomeRepository>();
        outcomes.DeliverVehicleAsync(ConvoyId, Vin, Arg.Any<CancellationToken>()).Returns(boxesDelivered);

        var manifests = Substitute.For<IManifestRepository>();
        manifests.GetForVehicleAsync(ConvoyId, Vin, Arg.Any<CancellationToken>()).Returns(manifest);

        return new World(new DeliverVehicleHandler(convoys, truckList, outcomes, manifests), outcomes, manifests);
    }

    private static World ATravellingVehicle(ManifestReadModel? manifest = null, int? boxesDelivered = 2) =>
        AWorld(ADepartedConvoy(), ConvoyTestData.AVehicle(Vin), manifest ?? AManifest(ManifestStatus.InTransit), boxesDelivered);

    [Fact]
    public async Task Delivers_the_vehicle_and_its_boxes()
    {
        var world = ATravellingVehicle(boxesDelivered: 3);

        var result = await world.Deliver();

        result.Outcome.Should().Be(DeliverVehicleOutcome.Delivered);
        result.BoxesDelivered.Should().Be(3);
    }

    [Fact]
    public async Task Moves_the_vehicles_manifest_to_delivered()
    {
        var world = ATravellingVehicle();

        await world.Deliver();

        await world.Manifests.Received(1).TransitionAsync(
            "MAN-1", ManifestStatus.InTransit, ManifestStatus.Delivered, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Leaves_a_manifest_that_cannot_reach_delivered_alone_and_still_delivers()
    {
        var world = ATravellingVehicle(AManifest(ManifestStatus.Preparing));

        (await world.Deliver()).Outcome.Should().Be(DeliverVehicleOutcome.Delivered);
        await world.Manifests.DidNotReceive().TransitionAsync(
            Arg.Any<string>(), Arg.Any<ManifestStatus>(), Arg.Any<ManifestStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_an_unknown_convoy()
    {
        (await AWorld().Deliver()).Outcome.Should().Be(DeliverVehicleOutcome.ConvoyNotFound);
    }

    [Fact]
    public async Task Refuses_a_convoy_that_has_not_departed()
    {
        var world = AWorld(ConvoyTestData.APublishedConvoy(), ConvoyTestData.AVehicle(Vin));

        (await world.Deliver()).Outcome.Should().Be(DeliverVehicleOutcome.ConvoyNotDeparted);
    }

    [Fact]
    public async Task Refuses_a_convoy_that_has_arrived()
    {
        var world = AWorld(ConvoyTestData.AnArrivedConvoy() with { DepartedAt = ConvoyTestData.Start }, ConvoyTestData.AVehicle(Vin));

        (await world.Deliver()).Outcome.Should().Be(DeliverVehicleOutcome.ConvoyArrived);
    }

    [Fact]
    public async Task Refuses_a_vehicle_that_is_not_on_the_convoy()
    {
        var world = AWorld(ADepartedConvoy(), entry: null);

        (await world.Deliver()).Outcome.Should().Be(DeliverVehicleOutcome.VehicleNotOnConvoy);
    }

    [Fact]
    public async Task Refuses_a_withdrawn_vehicle()
    {
        var world = AWorld(ADepartedConvoy(), ConvoyTestData.AVehicle(Vin) with { WithdrawnAt = ConvoyTestData.Start });

        (await world.Deliver()).Outcome.Should().Be(DeliverVehicleOutcome.VehicleWithdrawn);
    }

    [Fact]
    public async Task Refuses_a_vehicle_already_delivered_and_does_not_touch_its_manifest()
    {
        var world = AWorld(
            ADepartedConvoy(), ConvoyTestData.AVehicle(Vin) with { DeliveredAt = ConvoyTestData.Start });

        (await world.Deliver()).Outcome.Should().Be(DeliverVehicleOutcome.AlreadyDelivered);
        await world.Outcomes.DidNotReceive().DeliverVehicleAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_already_delivered_when_another_writer_stamped_it_first()
    {
        var world = ATravellingVehicle(boxesDelivered: null);

        (await world.Deliver()).Outcome.Should().Be(DeliverVehicleOutcome.AlreadyDelivered);
        await world.Manifests.DidNotReceive().TransitionAsync(
            Arg.Any<string>(), Arg.Any<ManifestStatus>(), Arg.Any<ManifestStatus>(), Arg.Any<CancellationToken>());
    }
}
