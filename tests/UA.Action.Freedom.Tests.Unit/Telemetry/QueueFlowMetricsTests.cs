using System.Diagnostics.Metrics;
using AwesomeAssertions;
using UA.Action.Freedom.Telemetry;

namespace UA.Action.Freedom.Tests.Unit.Telemetry;

public sealed class QueueFlowMetricsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly Meter _meter = new(QueueFlowMetrics.MeterName);
    private readonly MetricCapture _capture;
    private readonly QueueFlowMetrics _metrics;

    public QueueFlowMetricsTests()
    {
        _capture = new MetricCapture(_meter);
        _metrics = new QueueFlowMetrics(_meter, new FixedTime(Now));
    }

    public void Dispose()
    {
        _capture.Dispose();
        _meter.Dispose();
    }

    [Theory]
    [InlineData(QueueOutcome.Completed, "completed")]
    [InlineData(QueueOutcome.DeadLettered, "dead_lettered")]
    [InlineData(QueueOutcome.LeftForRetry, "left_for_retry")]
    public void Settling_a_message_counts_it_against_its_queue_and_outcome(QueueOutcome outcome, string label)
    {
        _metrics.Settled("customs-work", outcome);

        _capture.Sum("freedom.queue.messages.processed", ("queue", "customs-work"), ("outcome", label))
            .Should().Be(1);
    }

    [Fact]
    public void Receiving_a_message_records_how_long_it_waited()
    {
        _metrics.Received("manifest-documents", insertedOn: Now.AddSeconds(-90), dequeueCount: 1);

        var age = _capture.Of("freedom.queue.message.age").Should().ContainSingle().Subject;
        age.Value.Should().Be(90);
        age.Tags["queue"].Should().Be("manifest-documents");
    }

    [Fact]
    public void A_message_the_queue_gave_no_timestamp_for_records_no_age()
    {
        _metrics.Received("manifest-documents", insertedOn: null, dequeueCount: 1);

        _capture.Of("freedom.queue.message.age").Should().BeEmpty();
    }

    [Fact]
    public void A_clock_skewed_into_the_future_never_records_a_negative_age()
    {
        _metrics.Received("customs-work", insertedOn: Now.AddSeconds(5), dequeueCount: 1);

        _capture.Of("freedom.queue.message.age").Single().Value.Should().Be(0);
    }

    [Fact]
    public void A_first_delivery_is_not_a_redelivery()
    {
        _metrics.Received("customs-work", Now, dequeueCount: 1);

        _capture.Of("freedom.queue.redeliveries").Should().BeEmpty();
    }

    [Fact]
    public void A_second_delivery_is_counted_as_a_redelivery()
    {
        _metrics.Received("customs-work", Now, dequeueCount: 2);

        _capture.Sum("freedom.queue.redeliveries", ("queue", "customs-work")).Should().Be(1);
    }

    [Theory]
    [InlineData(true, "ok")]
    [InlineData(false, "failed")]
    public void Enqueueing_is_counted_by_result(bool succeeded, string label)
    {
        _metrics.Enqueued("customs-work", succeeded);

        _capture.Sum("freedom.queue.enqueue", ("queue", "customs-work"), ("result", label)).Should().Be(1);
    }
}
