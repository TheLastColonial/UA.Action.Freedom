using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Domain;
using UA.Action.Freedom.Tests.Unit.People;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// Marking a convoy arrived, and what an arrived convoy then refuses: once the journey is over
/// its crew, insurance and vehicles are history, not plans.
/// </summary>
public class ArrivalHandlerTests
{
    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly DateTime Arrived = new(2026, 9, 5, 17, 0, 0, DateTimeKind.Utc);

    private static IConvoyRepository ARepository(ConvoyReadModel? convoy)
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(convoy);
        repository.ExistsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(convoy is not null);
        return repository;
    }

    private static Task<ArriveConvoyResult> ArriveAsync(IConvoyRepository repository) =>
        new ArriveConvoyHandler(repository).HandleAsync(
            new ArriveConvoyCommand(ConvoyTestData.Id), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Arrives_a_published_convoy_whose_vehicles_have_all_finished()
    {
        var repository = ARepository(ConvoyTestData.APublishedConvoy());
        repository.ArriveAsync(ConvoyTestData.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(ArriveResult.Arrived);

        var result = await ArriveAsync(repository);

        result.Outcome.Should().Be(ArriveConvoyOutcome.Arrived);
    }

    [Fact]
    public async Task Names_the_vehicles_still_travelling()
    {
        var repository = ARepository(ConvoyTestData.APublishedConvoy());
        repository.ArriveAsync(ConvoyTestData.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(ArriveResult.VehiclesStillTravelling);
        repository.ListVehiclesStillTravellingAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([Vin]);

        var result = await ArriveAsync(repository);

        result.Outcome.Should().Be(ArriveConvoyOutcome.VehiclesStillTravelling);
        result.StillTravelling.Should().Equal(Vin);
    }

    [Fact]
    public async Task Refuses_a_convoy_whose_truck_list_was_never_published()
    {
        var repository = ARepository(ConvoyTestData.AReadModel());

        var result = await ArriveAsync(repository);

        result.Outcome.Should().Be(ArriveConvoyOutcome.TruckListNotPublished);
        await repository.DidNotReceive().ArriveAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_an_arrival_already_recorded()
    {
        var repository = ARepository(ConvoyTestData.APublishedConvoy() with { ArrivedAt = Arrived });

        var result = await ArriveAsync(repository);

        result.Outcome.Should().Be(ArriveConvoyOutcome.AlreadyArrived);
    }

    [Fact]
    public async Task Reports_an_unknown_convoy()
    {
        var result = await ArriveAsync(ARepository(convoy: null));

        result.Outcome.Should().Be(ArriveConvoyOutcome.NotFound);
    }

    [Fact]
    public async Task An_arrived_convoy_takes_no_crew_changes()
    {
        var repository = ARepository(ConvoyTestData.APublishedConvoy() with { ArrivedAt = Arrived });
        var people = Substitute.For<IPersonRepository>();
        people.GetByIdAsync(PersonTestData.Id, Arg.Any<CancellationToken>()).Returns(PersonTestData.AReadModel(isDriver: true));

        var assign = await new AssignDriverToVehicleHandler(repository, people).HandleAsync(
            new AssignDriverToVehicleCommand(ConvoyTestData.Id, Vin, PersonTestData.Id), TestContext.Current.CancellationToken);
        var unassign = await new UnassignDriverFromVehicleHandler(repository).HandleAsync(
            new UnassignDriverFromVehicleCommand(ConvoyTestData.Id, Vin, PersonTestData.Id), TestContext.Current.CancellationToken);

        assign.Should().Be(AssignDriverOutcome.ConvoyArrived);
        unassign.Should().Be(UnassignDriverOutcome.ConvoyArrived);
        await repository.DidNotReceive().AssignDriverAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CrewRole>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_arrived_convoy_takes_no_insurance_changes()
    {
        var repository = ARepository(ConvoyTestData.APublishedConvoy() with { ArrivedAt = Arrived });
        var policy = new VehicleInsuranceRecord(
            ConvoyTestData.Id, Vin, "Ukraine Aid Mutual", "POL-1", Arrived.AddDays(-10), Arrived.AddDays(10), null, "sub");

        var record = await new RecordInsuranceHandler(repository).HandleAsync(
            new RecordInsuranceCommand(policy), TestContext.Current.CancellationToken);

        record.Should().Be(RecordInsuranceOutcome.ConvoyArrived);
    }

    [Fact]
    public async Task A_handed_over_vehicle_is_refused_a_convoy()
    {
        var repository = ARepository(ConvoyTestData.AReadModel());
        repository.AssignVehicleAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns(AssignVehicleResult.HandedOver);

        var outcome = await new AssignVehicleToConvoyHandler(repository).HandleAsync(
            new AssignVehicleToConvoyCommand(ConvoyTestData.Id, Vin), TestContext.Current.CancellationToken);

        outcome.Should().Be(AssignVehicleOutcome.VehicleHandedOver);
    }
}
