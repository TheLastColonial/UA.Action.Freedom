using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Domain;
using UA.Action.Freedom.Tests.Unit.People;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// Crewing a vehicle on a convoy, per leg. The handler checks the convoy, the truck-list entry and
/// the volunteer; whether the seat is free is settled by the write.
/// </summary>
/// <remarks>
/// This is the only crewing there is. A manifest used to keep its own driver teams alongside these
/// rows, set through a different endpoint with a different rule and connected to them by nothing —
/// so the printed document could name a crew the insurance had never heard of.
/// </remarks>
public class VehicleCrewHandlerTests
{
    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly Guid PersonId = PersonTestData.Id;

    private static (IConvoyRepository Convoys, IConvoyVehicleRepository TruckList, IPersonRepository People) Repositories(
        ConvoyReadModel? convoy, PersonReadModel? person, ConvoyVehicleReadModel? entry = null)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(convoy);

        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.GetAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>())
            .Returns(entry ?? ConvoyTestData.AVehicle(Vin));

        var people = Substitute.For<IPersonRepository>();
        people.GetByIdAsync(PersonId, Arg.Any<CancellationToken>()).Returns(person);

        return (convoys, truckList, people);
    }

    private static Task<AssignCrewOutcome> AssignAsync(
        IConvoyRepository convoys,
        IConvoyVehicleRepository truckList,
        IPersonRepository people,
        JourneyLeg leg = JourneyLeg.Uk,
        CrewRole role = CrewRole.Driver) =>
        new AssignCrewToVehicleHandler(convoys, truckList, people).HandleAsync(
            new AssignCrewToVehicleCommand(ConvoyTestData.Id, Vin, PersonId, leg, role),
            TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(AssignCrewResult.Assigned, AssignCrewOutcome.Assigned)]
    [InlineData(AssignCrewResult.AlreadyAssigned, AssignCrewOutcome.AlreadyAssigned)]
    [InlineData(AssignCrewResult.VehicleNotOnConvoy, AssignCrewOutcome.VehicleNotFound)]
    [InlineData(AssignCrewResult.OnAnotherVehicle, AssignCrewOutcome.OnAnotherVehicle)]
    public async Task Reports_what_the_write_found(AssignCrewResult result, AssignCrewOutcome expected)
    {
        var (convoys, truckList, people) =
            Repositories(ConvoyTestData.AReadModel(), PersonTestData.AReadModel(PersonId, isDriver: true));
        truckList.AssignCrewAsync(ConvoyTestData.Id, Vin, PersonId, JourneyLeg.Uk, CrewRole.Driver, Arg.Any<CancellationToken>())
            .Returns(result);

        var outcome = await AssignAsync(convoys, truckList, people);

        outcome.Should().Be(expected);
    }

    [Fact]
    public async Task Crews_each_leg_separately()
    {
        // A vehicle is crewed twice, with a handover at the European border: the same volunteer
        // taking both halves is ordinary, and so is a different crew taking the second.
        var (convoys, truckList, people) =
            Repositories(ConvoyTestData.AReadModel(), PersonTestData.AReadModel(PersonId, isDriver: true));
        truckList.AssignCrewAsync(
                ConvoyTestData.Id, Vin, PersonId, JourneyLeg.Border, CrewRole.Driver, Arg.Any<CancellationToken>())
            .Returns(AssignCrewResult.Assigned);

        var outcome = await AssignAsync(convoys, truckList, people, JourneyLeg.Border);

        outcome.Should().Be(AssignCrewOutcome.Assigned);
        await truckList.Received(1).AssignCrewAsync(
            ConvoyTestData.Id, Vin, PersonId, JourneyLeg.Border, CrewRole.Driver, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_the_convoy_missing_before_looking_at_anything_else()
    {
        var (convoys, truckList, people) =
            Repositories(convoy: null, PersonTestData.AReadModel(PersonId, isDriver: true));

        var outcome = await AssignAsync(convoys, truckList, people);

        outcome.Should().Be(AssignCrewOutcome.ConvoyNotFound);
        await NeverWrote(truckList);
    }

    [Fact]
    public async Task Refuses_to_crew_an_arrived_convoy()
    {
        var (convoys, truckList, people) = Repositories(
            ConvoyTestData.AnArrivedConvoy(), PersonTestData.AReadModel(PersonId, isDriver: true));

        var outcome = await AssignAsync(convoys, truckList, people);

        outcome.Should().Be(AssignCrewOutcome.ConvoyArrived);
        await NeverWrote(truckList);
    }

    [Fact]
    public async Task Refuses_to_crew_a_vehicle_that_has_withdrawn()
    {
        // It broke down and left; there is no leg left for anybody to drive.
        var (convoys, truckList, people) = Repositories(
            ConvoyTestData.AReadModel(),
            PersonTestData.AReadModel(PersonId, isDriver: true),
            ConvoyTestData.AWithdrawnVehicle(Vin));

        var outcome = await AssignAsync(convoys, truckList, people);

        outcome.Should().Be(AssignCrewOutcome.VehicleWithdrawn);
        await NeverWrote(truckList);
    }

    [Fact]
    public async Task Reports_a_vehicle_that_is_not_on_the_truck_list()
    {
        var (convoys, truckList, people) =
            Repositories(ConvoyTestData.AReadModel(), PersonTestData.AReadModel(PersonId, isDriver: true));
        truckList.GetAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns((ConvoyVehicleReadModel?)null);

        var outcome = await AssignAsync(convoys, truckList, people);

        outcome.Should().Be(AssignCrewOutcome.VehicleNotFound);
        await NeverWrote(truckList);
    }

    [Fact]
    public async Task Reports_an_unknown_volunteer()
    {
        var (convoys, truckList, people) = Repositories(ConvoyTestData.AReadModel(), person: null);

        var outcome = await AssignAsync(convoys, truckList, people);

        outcome.Should().Be(AssignCrewOutcome.PersonNotFound);
        await NeverWrote(truckList);
    }

    [Fact]
    public async Task Refuses_a_volunteer_who_is_not_a_driver()
    {
        var (convoys, truckList, people) =
            Repositories(ConvoyTestData.AReadModel(), PersonTestData.AReadModel(PersonId, isDriver: false));

        var outcome = await AssignAsync(convoys, truckList, people);

        outcome.Should().Be(AssignCrewOutcome.PersonNotADriver);
        await NeverWrote(truckList);
    }

    [Fact]
    public async Task Any_volunteer_may_ride_as_a_passenger()
    {
        var (convoys, truckList, people) =
            Repositories(ConvoyTestData.AReadModel(), PersonTestData.AReadModel(PersonId, isDriver: false));
        truckList.AssignCrewAsync(
                ConvoyTestData.Id, Vin, PersonId, JourneyLeg.Uk, CrewRole.Passenger, Arg.Any<CancellationToken>())
            .Returns(AssignCrewResult.Assigned);

        var outcome = await AssignAsync(convoys, truckList, people, role: CrewRole.Passenger);

        outcome.Should().Be(AssignCrewOutcome.Assigned);
    }

    [Fact]
    public async Task Unassigns_a_crew_member_from_one_leg()
    {
        var (convoys, truckList, _) = Repositories(ConvoyTestData.AReadModel(), person: null);
        truckList.UnassignCrewAsync(ConvoyTestData.Id, Vin, PersonId, JourneyLeg.Border, Arg.Any<CancellationToken>())
            .Returns(true);

        var outcome = await UnassignAsync(convoys, truckList, JourneyLeg.Border);

        outcome.Should().Be(UnassignCrewOutcome.Unassigned);
    }

    [Fact]
    public async Task Unassigning_from_a_vehicle_not_on_the_convoy_says_so()
    {
        var (convoys, truckList, _) = Repositories(ConvoyTestData.AReadModel(), person: null);
        truckList.GetAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns((ConvoyVehicleReadModel?)null);

        var outcome = await UnassignAsync(convoys, truckList);

        outcome.Should().Be(UnassignCrewOutcome.NotOnThisConvoy);
    }

    [Fact]
    public async Task Unassigning_somebody_who_is_not_crewing_that_leg_says_so()
    {
        var (convoys, truckList, _) = Repositories(ConvoyTestData.AReadModel(), person: null);
        truckList.UnassignCrewAsync(ConvoyTestData.Id, Vin, PersonId, JourneyLeg.Uk, Arg.Any<CancellationToken>())
            .Returns(false);

        var outcome = await UnassignAsync(convoys, truckList);

        outcome.Should().Be(UnassignCrewOutcome.NotAssigned);
    }

    [Fact]
    public async Task Unassigning_on_an_unknown_convoy_says_so()
    {
        var (convoys, truckList, _) = Repositories(convoy: null, person: null);

        var outcome = await UnassignAsync(convoys, truckList);

        outcome.Should().Be(UnassignCrewOutcome.ConvoyNotFound);
    }

    [Fact]
    public async Task Unassigning_on_an_arrived_convoy_says_so()
    {
        var (convoys, truckList, _) = Repositories(ConvoyTestData.AnArrivedConvoy(), person: null);

        var outcome = await UnassignAsync(convoys, truckList);

        outcome.Should().Be(UnassignCrewOutcome.ConvoyArrived);
    }

    [Fact]
    public async Task Lists_the_crew_of_a_vehicle()
    {
        var crew = new[]
        {
            new VehicleCrewReadModel(PersonId, "Olena", "Kovalenko", JourneyLeg.Uk, CrewRole.Driver),
        };
        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.ListCrewAsync(ConvoyTestData.Id, Vin, null, Arg.Any<CancellationToken>()).Returns(crew);

        var members = await new ListVehicleCrewHandler(truckList).HandleAsync(
            new ListVehicleCrewQuery(ConvoyTestData.Id, Vin), TestContext.Current.CancellationToken);

        members.Should().BeEquivalentTo(crew);
    }

    [Fact]
    public async Task Lists_the_crew_of_one_leg_when_asked()
    {
        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.ListCrewAsync(ConvoyTestData.Id, Vin, JourneyLeg.Border, Arg.Any<CancellationToken>()).Returns([]);

        await new ListVehicleCrewHandler(truckList).HandleAsync(
            new ListVehicleCrewQuery(ConvoyTestData.Id, Vin, JourneyLeg.Border), TestContext.Current.CancellationToken);

        await truckList.Received(1).ListCrewAsync(
            ConvoyTestData.Id, Vin, JourneyLeg.Border, Arg.Any<CancellationToken>());
    }

    private static Task<UnassignCrewOutcome> UnassignAsync(
        IConvoyRepository convoys, IConvoyVehicleRepository truckList, JourneyLeg leg = JourneyLeg.Uk) =>
        new UnassignCrewFromVehicleHandler(convoys, truckList).HandleAsync(
            new UnassignCrewFromVehicleCommand(ConvoyTestData.Id, Vin, PersonId, leg),
            TestContext.Current.CancellationToken);

    private static Task NeverWrote(IConvoyVehicleRepository truckList) =>
        truckList.DidNotReceive().AssignCrewAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<JourneyLeg>(), Arg.Any<CrewRole>(),
            Arg.Any<CancellationToken>());
}
