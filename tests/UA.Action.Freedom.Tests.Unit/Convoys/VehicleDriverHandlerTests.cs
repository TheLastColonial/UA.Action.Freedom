using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Tests.Unit.People;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// Crewing a vehicle on a convoy. The handler checks the convoy and the volunteer; whether the
/// vehicle is on this convoy and whether the driver is already on it is settled by the write.
/// </summary>
public class VehicleDriverHandlerTests
{
    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly Guid PersonId = PersonTestData.Id;

    private static (IConvoyRepository Convoys, IPersonRepository People) Repositories(
        ConvoyReadModel? convoy, PersonReadModel? person)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(convoy);
        var people = Substitute.For<IPersonRepository>();
        people.GetByIdAsync(PersonId, Arg.Any<CancellationToken>()).Returns(person);
        return (convoys, people);
    }

    private static Task<AssignDriverOutcome> AssignAsync(IConvoyRepository convoys, IPersonRepository people) =>
        new AssignDriverToVehicleHandler(convoys, people).HandleAsync(
            new AssignDriverToVehicleCommand(ConvoyTestData.Id, Vin, PersonId), TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(AssignDriverResult.Assigned, AssignDriverOutcome.Assigned)]
    [InlineData(AssignDriverResult.AlreadyAssigned, AssignDriverOutcome.AlreadyAssigned)]
    [InlineData(AssignDriverResult.VehicleNotOnConvoy, AssignDriverOutcome.VehicleNotFound)]
    public async Task Reports_what_the_write_found(AssignDriverResult result, AssignDriverOutcome expected)
    {
        var (convoys, people) = Repositories(ConvoyTestData.AReadModel(), PersonTestData.AReadModel(PersonId, isDriver: true));
        convoys.AssignDriverAsync(ConvoyTestData.Id, Vin, PersonId, Arg.Any<CancellationToken>()).Returns(result);

        var outcome = await AssignAsync(convoys, people);

        outcome.Should().Be(expected);
    }

    [Fact]
    public async Task Reports_the_convoy_missing_before_looking_at_anything_else()
    {
        var (convoys, people) = Repositories(convoy: null, PersonTestData.AReadModel(PersonId, isDriver: true));

        var outcome = await AssignAsync(convoys, people);

        outcome.Should().Be(AssignDriverOutcome.ConvoyNotFound);
        await convoys.DidNotReceive().AssignDriverAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_an_unknown_volunteer()
    {
        var (convoys, people) = Repositories(ConvoyTestData.AReadModel(), person: null);

        var outcome = await AssignAsync(convoys, people);

        outcome.Should().Be(AssignDriverOutcome.PersonNotFound);
        await convoys.DidNotReceive().AssignDriverAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_a_volunteer_who_is_not_a_driver()
    {
        var (convoys, people) = Repositories(ConvoyTestData.AReadModel(), PersonTestData.AReadModel(PersonId, isDriver: false));

        var outcome = await AssignAsync(convoys, people);

        outcome.Should().Be(AssignDriverOutcome.PersonNotADriver);
        await convoys.DidNotReceive().AssignDriverAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unassigns_a_driver_from_a_vehicle_on_the_convoy()
    {
        var (convoys, _) = Repositories(ConvoyTestData.AReadModel(), person: null);
        convoys.UnassignDriverAsync(ConvoyTestData.Id, Vin, PersonId, Arg.Any<CancellationToken>()).Returns(true);
        convoys.ListVehicleDriversAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>())
            .Returns([new VehicleDriverReadModel(PersonId, "Olena", "Kovalenko")]);

        var outcome = await new UnassignDriverFromVehicleHandler(convoys).HandleAsync(
            new UnassignDriverFromVehicleCommand(ConvoyTestData.Id, Vin, PersonId), TestContext.Current.CancellationToken);

        outcome.Should().Be(UnassignDriverOutcome.Unassigned);
    }

    [Fact]
    public async Task Unassigning_from_a_vehicle_not_on_the_convoy_says_so()
    {
        var (convoys, _) = Repositories(ConvoyTestData.AReadModel(), person: null);
        convoys.ListVehicleDriversAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<VehicleDriverReadModel>?)null);

        var outcome = await new UnassignDriverFromVehicleHandler(convoys).HandleAsync(
            new UnassignDriverFromVehicleCommand(ConvoyTestData.Id, Vin, PersonId), TestContext.Current.CancellationToken);

        outcome.Should().Be(UnassignDriverOutcome.NotOnThisConvoy);
    }

    [Fact]
    public async Task Unassigning_a_driver_who_is_not_on_the_vehicle_says_so()
    {
        var (convoys, _) = Repositories(ConvoyTestData.AReadModel(), person: null);
        convoys.ListVehicleDriversAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns([]);
        convoys.UnassignDriverAsync(ConvoyTestData.Id, Vin, PersonId, Arg.Any<CancellationToken>()).Returns(false);

        var outcome = await new UnassignDriverFromVehicleHandler(convoys).HandleAsync(
            new UnassignDriverFromVehicleCommand(ConvoyTestData.Id, Vin, PersonId), TestContext.Current.CancellationToken);

        outcome.Should().Be(UnassignDriverOutcome.NotAssigned);
    }

    [Fact]
    public async Task Unassigning_on_an_unknown_convoy_says_so()
    {
        var (convoys, _) = Repositories(convoy: null, person: null);

        var outcome = await new UnassignDriverFromVehicleHandler(convoys).HandleAsync(
            new UnassignDriverFromVehicleCommand(ConvoyTestData.Id, Vin, PersonId), TestContext.Current.CancellationToken);

        outcome.Should().Be(UnassignDriverOutcome.ConvoyNotFound);
    }

    [Fact]
    public async Task Lists_the_crew_of_a_vehicle()
    {
        var crew = new[] { new VehicleDriverReadModel(PersonId, "Olena", "Kovalenko") };
        var (convoys, _) = Repositories(ConvoyTestData.AReadModel(), person: null);
        convoys.ListVehicleDriversAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns(crew);

        var drivers = await new ListVehicleDriversHandler(convoys).HandleAsync(
            new ListVehicleDriversQuery(ConvoyTestData.Id, Vin), TestContext.Current.CancellationToken);

        drivers.Should().BeEquivalentTo(crew);
    }
}
