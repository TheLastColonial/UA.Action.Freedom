using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// The truck list: putting vehicles on it, publishing it, and taking a vehicle off — or, once it
/// is published, recording that one left.
/// </summary>
/// <remarks>
/// docs/process.puml orders the work <em>Truck List Created → Truck List Published → Manifest
/// Proposed</em>, and docs/domain/key-concepts.md describes the truck list as "the set of
/// vehicles committed to the next convoy, published so manifests can be proposed against it".
/// Manifests are therefore proposed against a fixed set, and publication closes the list to
/// <em>additions</em>.
///
/// <para>
/// It does not close it to departures, because vehicles break down. A vehicle that leaves after
/// publication is <em>withdrawn</em>: its row, its crew, its insurance and its manifest all stay,
/// because the manifest goes on describing a load that is real and the record of which convoy it
/// set off with is part of what happened.
/// </para>
/// </remarks>
public class TruckListHandlerTests
{
    private const string Vin = "WVWZZZ1JZXW000001";

    private static (IConvoyRepository Convoys, IConvoyVehicleRepository TruckList) Repositories(ConvoyReadModel? convoy)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(convoy);
        return (convoys, Substitute.For<IConvoyVehicleRepository>());
    }

    private static Task<AssignVehicleOutcome> AssignAsync(
        IConvoyRepository convoys, IConvoyVehicleRepository truckList, string vin = Vin) =>
        new AssignVehicleToConvoyHandler(convoys, truckList).HandleAsync(
            new AssignVehicleToConvoyCommand(ConvoyTestData.Id, vin), TestContext.Current.CancellationToken);

    private static Task<UnassignVehicleOutcome> UnassignAsync(
        IConvoyRepository convoys, IConvoyVehicleRepository truckList, string? reason = null) =>
        new UnassignVehicleFromConvoyHandler(convoys, truckList).HandleAsync(
            new UnassignVehicleFromConvoyCommand(ConvoyTestData.Id, Vin, reason),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task Publishes_the_truck_list_of_a_convoy_that_has_not_published_one()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.PublishTruckListAsync(ConvoyTestData.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var outcome = await new PublishTruckListHandler(repository).HandleAsync(
            new PublishTruckListCommand(ConvoyTestData.Id), TestContext.Current.CancellationToken);

        outcome.Should().Be(PublishTruckListOutcome.Published);
    }

    [Fact]
    public async Task Reports_not_found_when_there_is_no_such_convoy()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.PublishTruckListAsync(ConvoyTestData.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);
        repository.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns((ConvoyReadModel?)null);

        var outcome = await new PublishTruckListHandler(repository).HandleAsync(
            new PublishTruckListCommand(ConvoyTestData.Id), TestContext.Current.CancellationToken);

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

        var outcome = await new PublishTruckListHandler(repository).HandleAsync(
            new PublishTruckListCommand(ConvoyTestData.Id), TestContext.Current.CancellationToken);

        outcome.Should().Be(PublishTruckListOutcome.AlreadyPublished);
    }

    [Theory]
    [InlineData(AddToTruckListResult.Added, AssignVehicleOutcome.Assigned)]
    [InlineData(AddToTruckListResult.AlreadyOnThisConvoy, AssignVehicleOutcome.Assigned)]
    [InlineData(AddToTruckListResult.VehicleNotFound, AssignVehicleOutcome.VehicleNotFound)]
    [InlineData(AddToTruckListResult.NotPassedInspection, AssignVehicleOutcome.VehicleNotPassedInspection)]
    [InlineData(AddToTruckListResult.OnAnotherConvoy, AssignVehicleOutcome.VehicleOnAnotherConvoy)]
    [InlineData(AddToTruckListResult.HandedOver, AssignVehicleOutcome.VehicleHandedOver)]
    public async Task Reports_what_the_write_found(AddToTruckListResult result, AssignVehicleOutcome expected)
    {
        // Adding a vehicle that is already on this convoy is not an error: the caller asked for a
        // state the truck list is already in, and the conditional INSERT is what settles the race.
        var (convoys, truckList) = Repositories(ConvoyTestData.AReadModel());
        truckList.AddAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns(result);

        var outcome = await AssignAsync(convoys, truckList);

        outcome.Should().Be(expected);
    }

    [Fact]
    public async Task Refuses_to_add_a_vehicle_once_the_truck_list_is_published()
    {
        var (convoys, truckList) = Repositories(ConvoyTestData.APublishedConvoy());

        var outcome = await AssignAsync(convoys, truckList);

        outcome.Should().Be(AssignVehicleOutcome.TruckListPublished);
        await truckList.DidNotReceive().AddAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_to_add_a_vehicle_to_an_arrived_convoy()
    {
        var (convoys, truckList) = Repositories(ConvoyTestData.AnArrivedConvoy());

        var outcome = await AssignAsync(convoys, truckList);

        outcome.Should().Be(AssignVehicleOutcome.ConvoyArrived);
        await truckList.DidNotReceive().AddAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_the_convoy_missing_when_assigning_to_one_that_does_not_exist()
    {
        var (convoys, truckList) = Repositories(convoy: null);

        var outcome = await AssignAsync(convoys, truckList);

        outcome.Should().Be(AssignVehicleOutcome.ConvoyNotFound);
    }

    [Fact]
    public async Task Removes_a_vehicle_outright_while_the_truck_list_is_still_open()
    {
        // Nothing downstream depends on the list yet, so the entry goes with its crew and insurance.
        var (convoys, truckList) = Repositories(ConvoyTestData.AReadModel());
        truckList.RemoveAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns(true);

        var outcome = await UnassignAsync(convoys, truckList);

        outcome.Should().Be(UnassignVehicleOutcome.Unassigned);
        await truckList.DidNotReceive().WithdrawAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removing_a_vehicle_that_is_not_on_the_convoy_says_so()
    {
        var (convoys, truckList) = Repositories(ConvoyTestData.AReadModel());
        truckList.RemoveAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns(false);

        var outcome = await UnassignAsync(convoys, truckList);

        outcome.Should().Be(UnassignVehicleOutcome.NotOnThisConvoy);
    }

    [Fact]
    public async Task Withdraws_a_vehicle_that_breaks_down_after_the_truck_list_is_published()
    {
        var (convoys, truckList) = Repositories(ConvoyTestData.APublishedConvoy());
        truckList.GetAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns(ConvoyTestData.AVehicle(Vin));
        truckList.WithdrawAsync(
                ConvoyTestData.Id, Vin, "Gearbox failure near Poznan", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var outcome = await UnassignAsync(convoys, truckList, "Gearbox failure near Poznan");

        outcome.Should().Be(UnassignVehicleOutcome.Withdrawn);

        // Nothing is deleted: the manifest and the Goods Movement Reference still describe a real
        // load, and the crew that set off is the record of who went.
        await truckList.DidNotReceive().RemoveAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Withdrawing_a_vehicle_that_is_not_on_the_convoy_says_so()
    {
        var (convoys, truckList) = Repositories(ConvoyTestData.APublishedConvoy());
        truckList.GetAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns((ConvoyVehicleReadModel?)null);

        var outcome = await UnassignAsync(convoys, truckList);

        outcome.Should().Be(UnassignVehicleOutcome.NotOnThisConvoy);
    }

    [Fact]
    public async Task Withdrawing_a_vehicle_twice_says_so()
    {
        var (convoys, truckList) = Repositories(ConvoyTestData.APublishedConvoy());
        truckList.GetAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>())
            .Returns(ConvoyTestData.AWithdrawnVehicle(Vin));

        var outcome = await UnassignAsync(convoys, truckList);

        outcome.Should().Be(UnassignVehicleOutcome.AlreadyWithdrawn);
        await truckList.DidNotReceive().WithdrawAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_to_take_a_vehicle_off_an_arrived_convoy()
    {
        // The journey is over and the truck list is the record of who went.
        var (convoys, truckList) = Repositories(ConvoyTestData.AnArrivedConvoy());

        var outcome = await UnassignAsync(convoys, truckList);

        outcome.Should().Be(UnassignVehicleOutcome.ConvoyArrived);
    }

    [Fact]
    public async Task Lists_the_truck_list_including_vehicles_that_withdrew()
    {
        // The list is the record of what set off, not only of what is still moving.
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.ExistsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(true);
        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.ListAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns([ConvoyTestData.AVehicle("VIN-1"), ConvoyTestData.AWithdrawnVehicle("VIN-2")]);

        var vehicles = await new ListConvoyVehiclesHandler(convoys, truckList).HandleAsync(
            new ListConvoyVehiclesQuery(ConvoyTestData.Id), TestContext.Current.CancellationToken);

        vehicles.Should().HaveCount(2);
        vehicles!.Single(vehicle => vehicle.Vin == "VIN-2").Withdrawn.Should().BeTrue();
    }

    [Fact]
    public async Task There_is_no_truck_list_for_a_convoy_that_does_not_exist()
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.ExistsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(false);

        var vehicles = await new ListConvoyVehiclesHandler(convoys, Substitute.For<IConvoyVehicleRepository>())
            .HandleAsync(new ListConvoyVehiclesQuery(ConvoyTestData.Id), TestContext.Current.CancellationToken);

        vehicles.Should().BeNull();
    }
}
