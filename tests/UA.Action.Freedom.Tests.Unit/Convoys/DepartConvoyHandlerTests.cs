using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// O36: one <c>depart</c> on the convoy, refused while a blocking requirement is outstanding. O3: nothing overrides it.
/// </summary>
public class DepartConvoyHandlerTests
{
    private static readonly DateTime Now = ConvoyTestData.Start.AddHours(1);

    private static Requirement Outstanding(string code = "leader") => new(
        code, RequirementScope.Convoy, null, RequirementState.ToDo, "Dispatcher", RequirementSeverity.Blocking, "No Convoy Leader assigned", "/x");

    private static ConvoyReadinessReadModel Readiness(params Requirement[] requirements) => new(
        requirements.All(r => r.State == RequirementState.Done || r.Severity == RequirementSeverity.Advisory), true, [], [])
    {
        Requirements = requirements,
    };

    private sealed class Setup
    {
        public IQueryHandler<GetConvoyReadinessQuery, ConvoyReadinessReadModel?> Readiness { get; } =
            Substitute.For<IQueryHandler<GetConvoyReadinessQuery, ConvoyReadinessReadModel?>>();
        public IConvoyRepository Convoys { get; } = Substitute.For<IConvoyRepository>();

        public Setup(ConvoyReadModel? convoy = null, ConvoyReadinessReadModel? readiness = null)
        {
            Convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(convoy ?? ConvoyTestData.APublishedConvoy());
            Readiness.HandleAsync(Arg.Any<GetConvoyReadinessQuery>(), Arg.Any<CancellationToken>()).Returns(readiness ?? Readiness_());
            Convoys.DepartAsync(ConvoyTestData.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        }

        private static ConvoyReadinessReadModel Readiness_() => ConvoyReadinessTestsData.AllDone;

        public Task<DepartConvoyResult> DepartAsync() =>
            new DepartConvoyHandler(Readiness, Convoys, new FixedClock(Now))
                .HandleAsync(new DepartConvoyCommand(ConvoyTestData.Id), TestContext.Current.CancellationToken);
    }

    private static class ConvoyReadinessTestsData
    {
        public static ConvoyReadinessReadModel AllDone { get; } = Readiness(
            new Requirement("leader", RequirementScope.Convoy, null, RequirementState.Done, "Dispatcher", RequirementSeverity.Blocking, "ok", "/x"),
            new Requirement("advisory.budget", RequirementScope.Convoy, null, RequirementState.Warning, "Dispatcher", RequirementSeverity.Advisory, "No budget set", "/x"));
    }

    [Fact]
    public async Task A_convoy_with_every_blocking_requirement_met_departs_and_an_advisory_does_not_stop_it()
    {
        var setup = new Setup();

        var result = await setup.DepartAsync();

        result.Outcome.Should().Be(DepartConvoyOutcome.Departed);
        await setup.Convoys.Received(1).DepartAsync(ConvoyTestData.Id, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_convoy_with_an_outstanding_blocking_requirement_is_refused_with_the_list_and_nothing_is_stamped()
    {
        var setup = new Setup(readiness: Readiness(Outstanding("leader"), Outstanding("route")));

        var result = await setup.DepartAsync();

        result.Outcome.Should().Be(DepartConvoyOutcome.NotReady);
        result.Unmet.Select(requirement => requirement.Code).Should().Equal("leader", "route");
        await setup.Convoys.DidNotReceive().DepartAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task There_is_nothing_to_depart_for_a_convoy_that_does_not_exist()
    {
        var setup = new Setup();
        setup.Convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns((ConvoyReadModel?)null);

        (await setup.DepartAsync()).Outcome.Should().Be(DepartConvoyOutcome.NotFound);
    }

    [Fact]
    public async Task A_convoy_that_has_departed_cannot_depart_again()
    {
        var setup = new Setup(convoy: ConvoyTestData.APublishedConvoy() with { DepartedAt = Now.AddHours(-1) });

        (await setup.DepartAsync()).Outcome.Should().Be(DepartConvoyOutcome.AlreadyDeparted);
        await setup.Convoys.DidNotReceive().DepartAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_convoy_that_has_arrived_cannot_depart()
    {
        var setup = new Setup(convoy: ConvoyTestData.AnArrivedConvoy());

        (await setup.DepartAsync()).Outcome.Should().Be(DepartConvoyOutcome.AlreadyDeparted);
    }

    [Fact]
    public async Task Losing_the_race_to_another_dispatcher_is_reported_as_already_departed()
    {
        var setup = new Setup();
        setup.Convoys.DepartAsync(ConvoyTestData.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);

        (await setup.DepartAsync()).Outcome.Should().Be(DepartConvoyOutcome.AlreadyDeparted);
    }
}
