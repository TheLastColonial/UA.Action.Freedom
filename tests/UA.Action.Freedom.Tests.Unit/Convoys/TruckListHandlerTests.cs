using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// Publishing the truck list, and what that freezes.
/// </summary>
/// <remarks>
/// docs/process.puml orders the work <em>Truck List Created → Truck List Published → Manifest
/// Proposed</em>, and docs/domain/key-concepts.md describes the truck list as "the set of
/// vehicles committed to the next convoy, published so manifests can be proposed against it".
/// Manifests are therefore proposed against a fixed set: if a vehicle could leave the convoy
/// afterwards, a manifest would go on referring to a vehicle that is no longer travelling, and
/// nobody would find out until loading day. So publication is one-way, and it closes the
/// convoy's vehicle list.
/// </remarks>
public class TruckListHandlerTests
{
    [Fact]
    public async Task Publishes_the_truck_list_of_a_convoy_that_has_not_published_one()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.PublishTruckListAsync(ConvoyTestData.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = new PublishTruckListHandler(repository);

        var outcome = await handler.HandleAsync(
            new PublishTruckListCommand(ConvoyTestData.Id), CancellationToken.None);

        outcome.Should().Be(PublishTruckListOutcome.Published);
    }

    [Fact]
    public async Task Reports_not_found_when_there_is_no_such_convoy()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.PublishTruckListAsync(ConvoyTestData.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);
        repository.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns((ConvoyReadModel?)null);
        var handler = new PublishTruckListHandler(repository);

        var outcome = await handler.HandleAsync(
            new PublishTruckListCommand(ConvoyTestData.Id), CancellationToken.None);

        outcome.Should().Be(PublishTruckListOutcome.NotFound);
    }

    [Fact]
    public async Task Refuses_to_publish_a_truck_list_twice()
    {
        // Republishing would silently move the goalposts under every manifest already proposed
        // against the first list.
        var repository = Substitute.For<IConvoyRepository>();
        repository.PublishTruckListAsync(ConvoyTestData.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);
        repository.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns(ConvoyTestData.APublishedConvoy());
        var handler = new PublishTruckListHandler(repository);

        var outcome = await handler.HandleAsync(
            new PublishTruckListCommand(ConvoyTestData.Id), CancellationToken.None);

        outcome.Should().Be(PublishTruckListOutcome.AlreadyPublished);
    }

    [Fact]
    public async Task Assigns_a_vehicle_to_a_convoy_whose_truck_list_is_still_open()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns(ConvoyTestData.AReadModel());
        repository.AssignVehicleAsync(ConvoyTestData.Id, "WVWZZZ1JZXW000001", Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = new AssignVehicleToConvoyHandler(repository);

        var outcome = await handler.HandleAsync(
            new AssignVehicleToConvoyCommand(ConvoyTestData.Id, "WVWZZZ1JZXW000001"), CancellationToken.None);

        outcome.Should().Be(AssignVehicleOutcome.Assigned);
    }

    [Fact]
    public async Task Refuses_to_add_a_vehicle_once_the_truck_list_is_published()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns(ConvoyTestData.APublishedConvoy());
        var handler = new AssignVehicleToConvoyHandler(repository);

        var outcome = await handler.HandleAsync(
            new AssignVehicleToConvoyCommand(ConvoyTestData.Id, "WVWZZZ1JZXW000001"), CancellationToken.None);

        outcome.Should().Be(AssignVehicleOutcome.TruckListPublished);
        await repository.DidNotReceive().AssignVehicleAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_to_remove_a_vehicle_once_the_truck_list_is_published()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns(ConvoyTestData.APublishedConvoy());
        var handler = new UnassignVehicleFromConvoyHandler(repository);

        var outcome = await handler.HandleAsync(
            new UnassignVehicleFromConvoyCommand(ConvoyTestData.Id, "WVWZZZ1JZXW000001"), CancellationToken.None);

        outcome.Should().Be(UnassignVehicleOutcome.TruckListPublished);
        await repository.DidNotReceive().UnassignVehicleAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_the_vehicle_missing_when_there_is_no_such_VIN()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns(ConvoyTestData.AReadModel());
        repository.AssignVehicleAsync(ConvoyTestData.Id, "NOSUCHVIN", Arg.Any<CancellationToken>())
            .Returns(false);
        var handler = new AssignVehicleToConvoyHandler(repository);

        var outcome = await handler.HandleAsync(
            new AssignVehicleToConvoyCommand(ConvoyTestData.Id, "NOSUCHVIN"), CancellationToken.None);

        outcome.Should().Be(AssignVehicleOutcome.VehicleNotFound);
    }

    [Fact]
    public async Task Reports_the_convoy_missing_when_assigning_to_one_that_does_not_exist()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns((ConvoyReadModel?)null);
        var handler = new AssignVehicleToConvoyHandler(repository);

        var outcome = await handler.HandleAsync(
            new AssignVehicleToConvoyCommand(ConvoyTestData.Id, "WVWZZZ1JZXW000001"), CancellationToken.None);

        outcome.Should().Be(AssignVehicleOutcome.ConvoyNotFound);
    }
}
