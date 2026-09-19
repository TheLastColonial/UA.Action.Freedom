using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace UA.Action.Freedom.Telemetry;

/// <summary>
/// Whether a worker's polling loops are still alive, which no other signal shows: a worker whose
/// loop has stopped looks exactly like one with nothing to do.
/// </summary>
/// <remarks>
/// A loop reports <see cref="Succeeded"/> on every pass that completes — including passes that
/// found nothing — so a heartbeat that goes stale means the loop is stuck or dead, not idle.
/// </remarks>
public sealed class WorkerLoopMetrics
{
    public const string MeterName = "UA.Action.Freedom.Worker";

    private readonly Counter<long> _errors;
    private readonly ConcurrentDictionary<string, long> _lastSuccess = new();
    private readonly TimeProvider _clock;

    public WorkerLoopMetrics(Meter meter, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;

        _errors = meter.CreateCounter<long>(
            "freedom.worker.loop.errors", "{error}", "Passes of a worker loop that ended in an unhandled error.");
        meter.CreateObservableGauge(
            "freedom.worker.loop.last_success",
            () => _lastSuccess.Select(entry => new Measurement<long>(
                entry.Value, new KeyValuePair<string, object?>("loop", entry.Key))),
            unit: "s",
            description: "When a worker loop last completed a pass, as unix seconds.");
    }

    public static WorkerLoopMetrics Create(IMeterFactory meterFactory) => new(meterFactory.Create(MeterName));

    /// <summary>For services built without a host: a real instance nothing listens to.</summary>
    public static WorkerLoopMetrics Unobserved { get; } = new(new Meter($"{MeterName}.Unobserved"));

    public void Succeeded(string loop) => _lastSuccess[loop] = _clock.GetUtcNow().ToUnixTimeSeconds();

    public void Failed(string loop) =>
        _errors.Add(1, new KeyValuePair<string, object?>("loop", loop));
}
