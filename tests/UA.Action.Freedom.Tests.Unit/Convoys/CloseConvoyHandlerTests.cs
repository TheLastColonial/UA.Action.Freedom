using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>Closing a convoy (O10, O25): after arrival, repeatable, and answered with the report.</summary>
public class CloseConvoyHandlerTests
{
    private static readonly ConvoyReportReadModel Report = ConvoyReports.Compose(
        new ConvoyReportFacts(ConvoyTestData.Id, ConvoyTestData.Arrived, null, null, []));

    private static (CloseConvoyHandler Handler, IConvoyRepository Convoys) AHandler(ConvoyReadModel? convoy, bool stamped = true)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(convoy);
        convoys.CloseAsync(ConvoyTestData.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(stamped);

        var report = Substitute.For<IQueryHandler<GetConvoyReportQuery, ConvoyReportReadModel?>>();
        report.HandleAsync(new GetConvoyReportQuery(ConvoyTestData.Id), Arg.Any<CancellationToken>()).Returns(Report);

        return (new CloseConvoyHandler(convoys, report), convoys);
    }

    private static Task<CloseConvoyResult> Close(CloseConvoyHandler handler) =>
        handler.HandleAsync(new CloseConvoyCommand(ConvoyTestData.Id), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Closes_an_arrived_convoy_and_returns_the_report()
    {
        var (handler, _) = AHandler(ConvoyTestData.AnArrivedConvoy());

        var result = await Close(handler);

        result.Outcome.Should().Be(CloseConvoyOutcome.Closed);
        result.Report.Should().Be(Report);
    }

    [Fact]
    public async Task Closes_an_already_closed_convoy_again_because_closing_does_not_lock()
    {
        var (handler, _) = AHandler(ConvoyTestData.AnArrivedConvoy() with { ClosedAt = ConvoyTestData.Arrived });

        (await Close(handler)).Outcome.Should().Be(CloseConvoyOutcome.Closed);
    }

    [Fact]
    public async Task Refuses_a_convoy_that_has_not_arrived_without_stamping_it()
    {
        var (handler, convoys) = AHandler(ConvoyTestData.APublishedConvoy());

        (await Close(handler)).Outcome.Should().Be(CloseConvoyOutcome.NotArrived);
        await convoys.DidNotReceive().CloseAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_an_unknown_convoy()
    {
        var (handler, _) = AHandler(convoy: null);

        (await Close(handler)).Outcome.Should().Be(CloseConvoyOutcome.NotFound);
    }

    [Fact]
    public async Task Reports_not_arrived_when_the_stamp_found_no_arrived_convoy()
    {
        var (handler, _) = AHandler(ConvoyTestData.AnArrivedConvoy(), stamped: false);

        (await Close(handler)).Outcome.Should().Be(CloseConvoyOutcome.NotArrived);
    }
}
