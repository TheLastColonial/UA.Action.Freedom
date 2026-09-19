using System.Diagnostics.Metrics;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Telemetry;

namespace UA.Action.Freedom.Tests.Unit.Telemetry;

public sealed class InstrumentedCommandHandlerTests : IDisposable
{
    private readonly Meter _meter = new(FreedomMetrics.MeterName);
    private readonly MetricCapture _capture;
    private readonly FreedomMetrics _metrics;

    public InstrumentedCommandHandlerTests()
    {
        _capture = new MetricCapture(_meter);
        _metrics = new FreedomMetrics(_meter);
    }

    public void Dispose()
    {
        _capture.Dispose();
        _meter.Dispose();
    }

    private sealed record DoSomethingCommand;

    public enum SomethingOutcome
    {
        Done,
        NotFound,
        Conflict,
    }

    private sealed class Handler<TResult>(Func<TResult> respond) : ICommandHandler<DoSomethingCommand, TResult>
    {
        public Task<TResult> HandleAsync(DoSomethingCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }

    private InstrumentedCommandHandler<DoSomethingCommand, TResult> Instrument<TResult>(Func<TResult> respond) =>
        new(new Handler<TResult>(respond), _metrics);

    [Fact]
    public async Task The_result_of_the_inner_handler_is_returned_unchanged()
    {
        var handler = Instrument(() => SomethingOutcome.Conflict);

        var outcome = await handler.HandleAsync(new DoSomethingCommand(), CancellationToken.None);

        outcome.Should().Be(SomethingOutcome.Conflict);
    }

    [Fact]
    public async Task A_command_is_counted_under_its_name_without_the_command_suffix()
    {
        await Instrument(() => SomethingOutcome.Done).HandleAsync(new DoSomethingCommand(), CancellationToken.None);

        _capture.Sum("freedom.handler.invocations", ("handler", "DoSomething")).Should().Be(1);
    }

    [Fact]
    public async Task The_first_member_of_an_outcome_enum_is_the_success_case()
    {
        await Instrument(() => SomethingOutcome.Done).HandleAsync(new DoSomethingCommand(), CancellationToken.None);

        _capture.Sum("freedom.handler.invocations", ("outcome", "Done"), ("result", "ok")).Should().Be(1);
    }

    [Theory]
    [InlineData(SomethingOutcome.NotFound)]
    [InlineData(SomethingOutcome.Conflict)]
    public async Task Any_other_member_is_a_refusal_and_keeps_its_name(SomethingOutcome refusal)
    {
        await Instrument(() => refusal).HandleAsync(new DoSomethingCommand(), CancellationToken.None);

        _capture.Sum("freedom.handler.invocations", ("outcome", refusal.ToString()), ("result", "rejected"))
            .Should().Be(1);
    }

    [Fact]
    public async Task A_result_that_wraps_an_outcome_is_classified_by_that_outcome()
    {
        var handler = Instrument(() => ArriveConvoyResult.Of(ArriveConvoyOutcome.VehiclesStillTravelling));

        await handler.HandleAsync(new DoSomethingCommand(), CancellationToken.None);

        _capture.Sum("freedom.handler.invocations", ("outcome", "VehiclesStillTravelling"), ("result", "rejected"))
            .Should().Be(1);
    }

    [Fact]
    public async Task A_null_result_means_nothing_was_found()
    {
        await Instrument<string?>(() => null).HandleAsync(new DoSomethingCommand(), CancellationToken.None);

        _capture.Sum("freedom.handler.invocations", ("outcome", "NoResult"), ("result", "rejected")).Should().Be(1);
    }

    [Fact]
    public async Task A_result_with_a_value_and_no_outcome_is_a_success()
    {
        await Instrument(() => 42).HandleAsync(new DoSomethingCommand(), CancellationToken.None);

        _capture.Sum("freedom.handler.invocations", ("outcome", "Succeeded"), ("result", "ok")).Should().Be(1);
    }

    [Fact]
    public async Task A_thrown_exception_is_counted_as_an_error_and_rethrown()
    {
        var handler = Instrument<SomethingOutcome>(() => throw new InvalidOperationException("password=hunter2"));

        var act = () => handler.HandleAsync(new DoSomethingCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _capture.Sum("freedom.handler.invocations", ("outcome", "Exception"), ("result", "error")).Should().Be(1);
    }

    [Fact]
    public async Task Nothing_of_a_thrown_exception_reaches_a_metric_or_a_span()
    {
        using var activities = new ActivityCapture(FreedomMetrics.MeterName);
        var handler = Instrument<SomethingOutcome>(() => throw new InvalidOperationException("password=hunter2"));

        await FluentActions.Awaiting(() => handler.HandleAsync(new DoSomethingCommand(), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        _capture.Measurements.SelectMany(m => m.Tags.Values).Should().NotContain(v => v!.ToString()!.Contains("hunter2"));
        activities.Stopped
            .Where(a => a.OperationName == "command DoSomething")
            .SelectMany(a => a.TagObjects.Select(t => t.Value?.ToString()).Append(a.StatusDescription))
            .Should().NotContain(v => v != null && v.Contains("hunter2"));
    }

    [Fact]
    public async Task A_cancelled_request_is_not_counted_as_a_failure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var handler = Instrument<SomethingOutcome>(() => throw new OperationCanceledException(cancelled.Token));

        await FluentActions.Awaiting(() => handler.HandleAsync(new DoSomethingCommand(), cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        _capture.Sum("freedom.handler.invocations", ("result", "error")).Should().Be(0);
        _capture.Sum("freedom.handler.invocations", ("outcome", "Cancelled"), ("result", "cancelled")).Should().Be(1);
    }

    [Fact]
    public async Task How_long_the_handler_took_is_recorded_in_seconds()
    {
        await Instrument(() => SomethingOutcome.Done).HandleAsync(new DoSomethingCommand(), CancellationToken.None);

        _capture.Of("freedom.handler.duration").Should().ContainSingle()
            .Which.Tags["handler"].Should().Be("DoSomething");
    }
}
