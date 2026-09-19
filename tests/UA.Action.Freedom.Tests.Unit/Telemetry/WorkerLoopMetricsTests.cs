using System.Diagnostics.Metrics;
using AwesomeAssertions;
using UA.Action.Freedom.Telemetry;

namespace UA.Action.Freedom.Tests.Unit.Telemetry;

public sealed class WorkerLoopMetricsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly Meter _meter = new(WorkerLoopMetrics.MeterName);
    private readonly MetricCapture _capture;
    private readonly FixedTime _clock = new(Now);
    private readonly WorkerLoopMetrics _metrics;

    public WorkerLoopMetricsTests()
    {
        _capture = new MetricCapture(_meter);
        _metrics = new WorkerLoopMetrics(_meter, _clock);
    }

    public void Dispose()
    {
        _capture.Dispose();
        _meter.Dispose();
    }

    [Fact]
    public void A_loop_that_has_never_succeeded_reports_no_heartbeat()
    {
        _capture.Observe();

        _capture.Of("freedom.worker.loop.last_success").Should().BeEmpty();
    }

    [Fact]
    public void A_successful_pass_records_when_it_happened_as_unix_seconds()
    {
        _metrics.Succeeded("drain");
        _capture.Observe();

        _capture.Sum("freedom.worker.loop.last_success", ("loop", "drain"))
            .Should().Be(Now.ToUnixTimeSeconds());
    }

    [Fact]
    public void Each_loop_keeps_its_own_heartbeat()
    {
        _metrics.Succeeded("drain");
        _clock.Advance(TimeSpan.FromMinutes(1));
        _metrics.Succeeded("outcomes");
        _capture.Observe();

        _capture.Sum("freedom.worker.loop.last_success", ("loop", "outcomes"))
            .Should().Be(Now.ToUnixTimeSeconds() + 60);
        _capture.Sum("freedom.worker.loop.last_success", ("loop", "drain"))
            .Should().Be(Now.ToUnixTimeSeconds());
    }

    [Fact]
    public void A_failed_pass_is_counted_and_leaves_the_last_success_alone()
    {
        _metrics.Succeeded("drain");
        _clock.Advance(TimeSpan.FromMinutes(5));
        _metrics.Failed("drain");
        _capture.Observe();

        _capture.Sum("freedom.worker.loop.errors", ("loop", "drain")).Should().Be(1);
        _capture.Sum("freedom.worker.loop.last_success", ("loop", "drain"))
            .Should().Be(Now.ToUnixTimeSeconds());
    }
}
