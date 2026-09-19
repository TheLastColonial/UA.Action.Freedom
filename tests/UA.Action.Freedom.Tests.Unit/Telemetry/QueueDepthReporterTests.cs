using System.Diagnostics.Metrics;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using UA.Action.Freedom.Telemetry;

namespace UA.Action.Freedom.Tests.Unit.Telemetry;

public sealed class QueueDepthReporterTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly WatchedQueue Customs = new("customs-work", "customs-work", "customs-work-poison");

    private readonly Meter _meter = new(QueueDepthReporter.MeterName);
    private readonly MetricCapture _capture;
    private readonly Dictionary<string, QueueSnapshot?> _snapshots = [];
    private readonly QueueDepthReporter _reporter;

    public QueueDepthReporterTests()
    {
        _capture = new MetricCapture(_meter);
        _reporter = new QueueDepthReporter(
            new StubProbe(_snapshots),
            [Customs],
            _meter,
            new FixedTime(Now),
            TimeSpan.FromSeconds(30),
            NullLogger<QueueDepthReporter>.Instance);
    }

    public void Dispose()
    {
        _capture.Dispose();
        _meter.Dispose();
    }

    [Fact]
    public async Task Work_and_poison_depth_are_reported_under_the_queues_logical_name()
    {
        _snapshots["customs-work"] = new QueueSnapshot(3, Now.AddSeconds(-45));
        _snapshots["customs-work-poison"] = new QueueSnapshot(1, Now.AddHours(-2));

        await _reporter.SampleOnceAsync(CancellationToken.None);
        _capture.Observe();

        _capture.Sum("freedom.queue.depth", ("queue", "customs-work"), ("kind", "work")).Should().Be(3);
        _capture.Sum("freedom.queue.depth", ("queue", "customs-work"), ("kind", "poison")).Should().Be(1);
    }

    [Fact]
    public async Task The_oldest_messages_age_is_measured_from_the_moment_the_gauge_is_read()
    {
        var clock = new FixedTime(Now);
        var reporter = new QueueDepthReporter(
            new StubProbe(_snapshots), [Customs], _meter, clock, TimeSpan.FromSeconds(30),
            NullLogger<QueueDepthReporter>.Instance);
        _snapshots["customs-work"] = new QueueSnapshot(1, Now.AddSeconds(-60));
        _snapshots["customs-work-poison"] = new QueueSnapshot(0, null);

        await reporter.SampleOnceAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(30));
        _capture.Observe();

        _capture.Sum("freedom.queue.oldest_message_age", ("queue", "customs-work"), ("kind", "work"))
            .Should().Be(90);
    }

    [Fact]
    public async Task An_empty_queue_reports_a_depth_of_zero_and_no_age()
    {
        _snapshots["customs-work"] = new QueueSnapshot(0, null);
        _snapshots["customs-work-poison"] = new QueueSnapshot(0, null);

        await _reporter.SampleOnceAsync(CancellationToken.None);
        _capture.Observe();

        _capture.Sum("freedom.queue.depth", ("queue", "customs-work"), ("kind", "work")).Should().Be(0);
        _capture.Of("freedom.queue.oldest_message_age").Should().BeEmpty();
    }

    [Fact]
    public async Task A_queue_that_cannot_be_read_reports_nothing_rather_than_a_stale_number()
    {
        _snapshots["customs-work"] = new QueueSnapshot(5, Now);
        _snapshots["customs-work-poison"] = new QueueSnapshot(0, null);
        await _reporter.SampleOnceAsync(CancellationToken.None);

        _snapshots["customs-work"] = null;
        await _reporter.SampleOnceAsync(CancellationToken.None);
        _capture.Observe();

        _capture.Of("freedom.queue.depth").Should().NotContain(m => (string?)m.Tags["kind"] == "work");
    }

    private sealed class StubProbe(Dictionary<string, QueueSnapshot?> snapshots) : IQueueDepthProbe
    {
        public Task<QueueSnapshot> SampleAsync(string queueName, CancellationToken cancellationToken) =>
            snapshots.GetValueOrDefault(queueName) is { } snapshot
                ? Task.FromResult(snapshot)
                : throw new InvalidOperationException("The queue could not be read.");
    }
}
