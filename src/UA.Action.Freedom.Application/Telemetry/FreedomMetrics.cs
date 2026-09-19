using System.Diagnostics;
using System.Diagnostics.Metrics;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Telemetry;

/// <summary>
/// The business-level counters of the Freedom Application: what its use cases decided, as opposed
/// to what HTTP did with the answer.
/// </summary>
/// <remarks>
/// Built on <c>System.Diagnostics.Metrics</c>, which is part of the runtime — this project takes
/// no OpenTelemetry dependency, and the host opts in with <c>AddMeter("UA.Action.Freedom.*")</c>.
/// <para>
/// <b>Every tag here is a bounded set</b> — a handler name, an outcome enum member, a
/// <see cref="ManifestStatus"/>. Nothing about a person, a receiver, a vehicle or a manifest
/// reference is ever a label: cardinality aside, a metric is retained and queried far more widely
/// than the record it counts.
/// </para>
/// </remarks>
public sealed class FreedomMetrics
{
    public const string MeterName = "UA.Action.Freedom.Application";

    /// <summary>The source spans for command handlers are started on.</summary>
    public const string SourceName = MeterName;

    public static readonly ActivitySource Source = new(SourceName);

    private static readonly double[] DurationBucketsSeconds = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];

    private readonly Counter<long> _invocations;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _transitions;
    private readonly Counter<long> _partialFailures;
    private readonly Counter<long> _receiverResolves;

    public FreedomMetrics(Meter meter)
    {
        _invocations = meter.CreateCounter<long>(
            "freedom.handler.invocations", "{invocation}", "Commands handled, by handler and what the handler decided.");
        _duration = meter.CreateHistogram(
            "freedom.handler.duration", "s", "How long a command handler took.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBucketsSeconds });
        _transitions = meter.CreateCounter<long>(
            "freedom.manifest.transitions", "{transition}", "Manifest status changes attempted, by edge and outcome.");
        _partialFailures = meter.CreateCounter<long>(
            "freedom.manifest.approve.partial_failures", "{failure}",
            "Approvals that froze a manifest and then could not hand its paperwork to a worker.");
        _receiverResolves = meter.CreateCounter<long>(
            "freedom.receiver.detail.resolves", "{resolve}",
            "Ground Officer lookups of a receiver's delivery address — aggregate only; the audit table says who and which.");
    }

    public static FreedomMetrics Create(IMeterFactory meterFactory) => new(meterFactory.Create(MeterName));

    /// <summary>
    /// For handlers built without a host (unit tests, tools): a real instance nothing listens to,
    /// so callers never null-check.
    /// </summary>
    public static FreedomMetrics Unobserved { get; } = new(new Meter($"{MeterName}.Unobserved"));

    public void HandlerCompleted(string handler, string outcome, string result, TimeSpan elapsed)
    {
        _invocations.Add(
            1,
            new KeyValuePair<string, object?>("handler", handler),
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("result", result));
        _duration.Record(
            elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("handler", handler),
            new KeyValuePair<string, object?>("result", result));
    }

    public void ManifestTransition<TOutcome>(ManifestStatus? from, ManifestStatus to, TOutcome outcome)
        where TOutcome : struct, Enum =>
        _transitions.Add(
            1,
            new KeyValuePair<string, object?>("from", from?.ToString() ?? "none"),
            new KeyValuePair<string, object?>("to", to.ToString()),
            new KeyValuePair<string, object?>("outcome", outcome.ToString()));

    /// <param name="stage"><c>gmr</c> or <c>document</c>: which hand-off failed.</param>
    public void ApproveFailedAfterFreeze(string stage) =>
        _partialFailures.Add(1, new KeyValuePair<string, object?>("stage", stage));

    public void ReceiverDetailResolved(bool found) =>
        _receiverResolves.Add(1, new KeyValuePair<string, object?>("result", found ? "found" : "not_found"));
}
