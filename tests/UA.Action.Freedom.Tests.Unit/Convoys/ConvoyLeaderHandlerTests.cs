using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// Nominating the Convoy Leader (D8, D17, P7): exactly one, a Driver crewed on the convoy, nominated by the
/// Dispatcher or Administrator, with the history kept.
/// </summary>
public class ConvoyLeaderHandlerTests
{
    private static readonly Guid Olena = new("0b7e8f2a-4c1d-4e5f-9a6b-7c8d9e0f1a2b");

    private static (IConvoyRepository Convoys, IConvoyLeaderRepository Leaders) AConvoy(bool arrived = false)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns(arrived ? ConvoyTestData.AnArrivedConvoy() : ConvoyTestData.AReadModel());
        return (convoys, Substitute.For<IConvoyLeaderRepository>());
    }

    [Fact]
    public async Task Nominates_a_driver_crewed_on_the_convoy()
    {
        var (convoys, leaders) = AConvoy();
        leaders.NominateAsync(ConvoyTestData.Id, Olena, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(NominateLeaderResult.Nominated);

        var outcome = await new NominateConvoyLeaderHandler(convoys, leaders).HandleAsync(
            new NominateConvoyLeaderCommand(ConvoyTestData.Id, Olena), CancellationToken.None);

        outcome.Should().Be(NominateConvoyLeaderOutcome.Nominated);
    }

    [Fact]
    public async Task Refuses_someone_who_is_not_a_driver_on_the_convoy()
    {
        // Not crewed at all, or only a passenger: the store reports both the same way.
        var (convoys, leaders) = AConvoy();
        leaders.NominateAsync(ConvoyTestData.Id, Olena, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(NominateLeaderResult.NotADriverOnConvoy);

        var outcome = await new NominateConvoyLeaderHandler(convoys, leaders).HandleAsync(
            new NominateConvoyLeaderCommand(ConvoyTestData.Id, Olena), CancellationToken.None);

        outcome.Should().Be(NominateConvoyLeaderOutcome.NotADriverOnConvoy);
    }

    [Fact]
    public async Task Nominating_the_current_leader_again_changes_nothing()
    {
        var (convoys, leaders) = AConvoy();
        leaders.NominateAsync(ConvoyTestData.Id, Olena, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(NominateLeaderResult.AlreadyLeader);

        var outcome = await new NominateConvoyLeaderHandler(convoys, leaders).HandleAsync(
            new NominateConvoyLeaderCommand(ConvoyTestData.Id, Olena), CancellationToken.None);

        outcome.Should().Be(NominateConvoyLeaderOutcome.AlreadyLeader);
    }

    [Fact]
    public async Task Reports_not_found_and_writes_nothing_for_an_unknown_convoy()
    {
        var convoys = Substitute.For<IConvoyRepository>();
        var leaders = Substitute.For<IConvoyLeaderRepository>();

        var outcome = await new NominateConvoyLeaderHandler(convoys, leaders).HandleAsync(
            new NominateConvoyLeaderCommand(ConvoyTestData.Id, Olena), CancellationToken.None);

        outcome.Should().Be(NominateConvoyLeaderOutcome.ConvoyNotFound);
        await leaders.DidNotReceive().NominateAsync(Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_a_nomination_once_the_convoy_has_arrived()
    {
        var (convoys, leaders) = AConvoy(arrived: true);

        var outcome = await new NominateConvoyLeaderHandler(convoys, leaders).HandleAsync(
            new NominateConvoyLeaderCommand(ConvoyTestData.Id, Olena), CancellationToken.None);

        outcome.Should().Be(NominateConvoyLeaderOutcome.ConvoyArrived);
        await leaders.DidNotReceive().NominateAsync(Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_leader_of_an_unknown_convoy_is_nothing()
    {
        var convoys = Substitute.For<IConvoyRepository>();

        var leader = await new GetConvoyLeaderHandler(convoys, Substitute.For<IConvoyLeaderRepository>())
            .HandleAsync(new GetConvoyLeaderQuery(ConvoyTestData.Id), CancellationToken.None);

        leader.Should().BeNull();
    }

    [Fact]
    public async Task The_current_leader_is_the_one_assignment_still_open_and_the_history_keeps_the_rest()
    {
        var (convoys, leaders) = AConvoy();
        convoys.ExistsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(true);
        var closed = new ConvoyLeaderAssignmentReadModel(1, ConvoyTestData.Id, Guid.NewGuid(), "A B", ConvoyTestData.Start, ConvoyTestData.Start.AddDays(1));
        var open = new ConvoyLeaderAssignmentReadModel(2, ConvoyTestData.Id, Olena, "Olena Bondar", ConvoyTestData.Start.AddDays(1), null);
        leaders.HistoryAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ConvoyLeaderAssignmentReadModel>>([open, closed]));

        var leader = await new GetConvoyLeaderHandler(convoys, leaders)
            .HandleAsync(new GetConvoyLeaderQuery(ConvoyTestData.Id), CancellationToken.None);

        leader!.Current.Should().Be(open);
        leader.History.Should().Equal(open, closed);
    }

    [Fact]
    public async Task A_convoy_with_no_leader_nominated_has_no_current_leader()
    {
        var (convoys, leaders) = AConvoy();
        convoys.ExistsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(true);

        var leader = await new GetConvoyLeaderHandler(convoys, leaders)
            .HandleAsync(new GetConvoyLeaderQuery(ConvoyTestData.Id), CancellationToken.None);

        leader!.Current.Should().BeNull();
        leader.History.Should().BeEmpty();
    }

    [Fact]
    public async Task The_leader_cannot_be_taken_off_the_crew_until_someone_else_leads()
    {
        var (convoys, leaders) = AConvoy();
        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.GetAsync(ConvoyTestData.Id, "VIN-1", Arg.Any<CancellationToken>()).Returns(ConvoyTestData.AVehicle());
        leaders.IsCurrentLeaderAsync(ConvoyTestData.Id, Olena, Arg.Any<CancellationToken>()).Returns(true);

        var outcome = await new UnassignCrewFromVehicleHandler(convoys, truckList, leaders).HandleAsync(
            new UnassignCrewFromVehicleCommand(ConvoyTestData.Id, "VIN-1", Olena), CancellationToken.None);

        outcome.Should().Be(UnassignCrewOutcome.IsConvoyLeader);
        await truckList.DidNotReceive().UnassignCrewAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
