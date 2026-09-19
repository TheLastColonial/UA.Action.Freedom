using System.Diagnostics.Metrics;

namespace UA.Action.Freedom.Telemetry;

/// <summary>How a worker settled a message it took off a queue.</summary>
public enum QueueOutcome
{
    /// <summary>The work is done and the message was deleted.</summary>
    Completed,

    /// <summary>The message can never succeed and was moved to the poison queue.</summary>
    DeadLettered,

    /// <summary>A transient failure; the message was left to reappear after its visibility timeout.</summary>
    LeftForRetry,
}

/// <summary>
/// Counters and histograms about queue traffic, shared by the Api (which enqueues) and both
/// workers (which drain).
/// </summary>
/// <remarks>
/// The only tag on any of them is the queue's logical name, or a bounded outcome. Nothing from a
/// message body is ever a label.
/// </remarks>
public sealed class QueueFlowMetrics
{
    public const string MeterName = "UA.Action.Freedom.Queue";

    private static readonly double[] AgeBucketsSeconds = [1, 5, 15, 30, 60, 120, 300, 900, 3600];

    private readonly Counter<long> _processed;
    private readonly Histogram<double> _age;
    private readonly Counter<long> _redeliveries;
    private readonly Counter<long> _enqueued;
    private readonly TimeProvider _clock;

    public QueueFlowMetrics(Meter meter, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;

        _processed = meter.CreateCounter<long>(
            "freedom.queue.messages.processed", "{message}", "Messages a worker took off a queue, by how they were settled.");
        _age = meter.CreateHistogram(
            "freedom.queue.message.age", "s",
            "How long a message had been on the queue when a worker picked it up.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = AgeBucketsSeconds });
        _redeliveries = meter.CreateCounter<long>(
            "freedom.queue.redeliveries", "{message}", "Messages picked up again after an earlier attempt did not settle them.");
        _enqueued = meter.CreateCounter<long>(
            "freedom.queue.enqueue", "{message}", "Messages the Freedom Application tried to put on a queue.");
    }

    public static QueueFlowMetrics Create(IMeterFactory meterFactory) => new(meterFactory.Create(MeterName));

    /// <summary>For processors built without a host (unit tests, tools): a real instance nothing listens to.</summary>
    public static QueueFlowMetrics Unobserved { get; } = new(new Meter($"{MeterName}.Unobserved"));

    public void Received(string queue, DateTimeOffset? insertedOn, long dequeueCount)
    {
        if (insertedOn is { } inserted)
        {
            var age = (_clock.GetUtcNow() - inserted).TotalSeconds;
            _age.Record(Math.Max(0, age), new KeyValuePair<string, object?>("queue", queue));
        }

        if (dequeueCount > 1)
        {
            _redeliveries.Add(1, new KeyValuePair<string, object?>("queue", queue));
        }
    }

    public void Settled(string queue, QueueOutcome outcome) =>
        _processed.Add(
            1,
            new KeyValuePair<string, object?>("queue", queue),
            new KeyValuePair<string, object?>("outcome", Label(outcome)));

    public void Enqueued(string queue, bool succeeded) =>
        _enqueued.Add(
            1,
            new KeyValuePair<string, object?>("queue", queue),
            new KeyValuePair<string, object?>("result", succeeded ? "ok" : "failed"));

    private static string Label(QueueOutcome outcome) => outcome switch
    {
        QueueOutcome.Completed => "completed",
        QueueOutcome.DeadLettered => "dead_lettered",
        QueueOutcome.LeftForRetry => "left_for_retry",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}
