using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Azure;
using Azure.Storage.Queues;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UA.Action.Freedom.Telemetry;

/// <summary>A queue to watch: its logical name (the metric label), and its work and poison queues.</summary>
public sealed record WatchedQueue(string Name, string WorkQueue, string PoisonQueue);

/// <param name="Depth">Approximate — Queue Storage counts invisible (in-flight) messages too.</param>
/// <param name="OldestInsertedOn">When the head of the queue was enqueued; null when it is empty.</param>
public sealed record QueueSnapshot(long Depth, DateTimeOffset? OldestInsertedOn);

/// <summary>Reads a queue's depth and head age. A port so the reporter is testable without a storage account.</summary>
public interface IQueueDepthProbe
{
    Task<QueueSnapshot> SampleAsync(string queueName, CancellationToken cancellationToken);
}

public sealed class AzureQueueDepthProbe(QueueServiceClient queues) : IQueueDepthProbe
{
    public async Task<QueueSnapshot> SampleAsync(string queueName, CancellationToken cancellationToken)
    {
        var queue = queues.GetQueueClient(queueName);

        try
        {
            var properties = await queue.GetPropertiesAsync(cancellationToken);
            var head = await queue.PeekMessagesAsync(1, cancellationToken);

            return new QueueSnapshot(
                properties.Value.ApproximateMessagesCount,
                head.Value.FirstOrDefault()?.InsertedOn);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // A poison queue is created on first use; until then it is, correctly, empty.
            return new QueueSnapshot(0, null);
        }
    }
}

/// <summary>
/// Publishes how many messages are waiting on each queue, and how old the oldest is, for the work
/// queue and its poison queue.
/// </summary>
/// <remarks>
/// A worker samples on a timer and the gauges read the cached result, because an observable
/// gauge's callback is synchronous and a storage call is not. It runs in the workers because they
/// are always up while there is work; the Api scales to zero and its gauges would vanish. A queue
/// that cannot be read reports <em>nothing</em> — an absent series is honest, a stale number is not.
/// </remarks>
public sealed class QueueDepthReporter : BackgroundService
{
    public const string MeterName = "UA.Action.Freedom.Queue.Depth";

    private readonly IQueueDepthProbe _probe;
    private readonly IReadOnlyList<WatchedQueue> _queues;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _interval;
    private readonly ILogger<QueueDepthReporter> _logger;
    private readonly ConcurrentDictionary<(string Queue, string Kind), QueueSnapshot> _latest = new();

    public QueueDepthReporter(
        IQueueDepthProbe probe,
        IReadOnlyList<WatchedQueue> queues,
        Meter meter,
        TimeProvider clock,
        TimeSpan interval,
        ILogger<QueueDepthReporter> logger)
    {
        _probe = probe;
        _queues = queues;
        _clock = clock;
        _interval = interval;
        _logger = logger;

        meter.CreateObservableGauge(
            "freedom.queue.depth",
            () => _latest.Select(entry => new Measurement<long>(entry.Value.Depth, Tags(entry.Key))),
            unit: "{message}",
            description: "Approximate messages on a queue, including those currently being processed.");
        meter.CreateObservableGauge(
            "freedom.queue.oldest_message_age",
            () => _latest
                .Where(entry => entry.Value.OldestInsertedOn is not null)
                .Select(entry => new Measurement<double>(
                    Math.Max(0, (_clock.GetUtcNow() - entry.Value.OldestInsertedOn!.Value).TotalSeconds),
                    Tags(entry.Key))),
            unit: "s",
            description: "Age of the message at the head of a queue.");
    }

    public async Task SampleOnceAsync(CancellationToken cancellationToken)
    {
        foreach (var queue in _queues)
        {
            await Sample(queue.Name, "work", queue.WorkQueue, cancellationToken);
            await Sample(queue.Name, "poison", queue.PoisonQueue, cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval, _clock);

        do
        {
            try
            {
                await SampleOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
        while (await SafeWait(timer, stoppingToken));
    }

    private async Task Sample(string name, string kind, string queueName, CancellationToken cancellationToken)
    {
        try
        {
            _latest[(name, kind)] = await _probe.SampleAsync(queueName, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _latest.TryRemove((name, kind), out _);
            _logger.LogWarning(
                "Could not read the {Kind} queue for {Queue} ({ExceptionType}); reporting nothing until it can be.",
                kind, name, exception.GetType().Name);
        }
    }

    private static KeyValuePair<string, object?>[] Tags((string Queue, string Kind) key) =>
    [
        new("queue", key.Queue),
        new("kind", key.Kind),
    ];

    private static async Task<bool> SafeWait(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
