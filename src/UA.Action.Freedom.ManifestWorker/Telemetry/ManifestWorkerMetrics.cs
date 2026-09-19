using System.Diagnostics.Metrics;

namespace UA.Action.Freedom.ManifestWorker.Telemetry;

/// <summary>
/// What rendering a manifest document costs, and how big the documents are.
/// </summary>
/// <remarks>
/// Aggregate numbers only. The document lists consignee organisations and regions, and none of
/// that — nor a manifest reference — is ever a tag.
/// </remarks>
public sealed class ManifestWorkerMetrics
{
    public const string MeterName = "UA.Action.Freedom.ManifestWorker";

    private static readonly double[] DurationBucketsSeconds = [0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];

    private static readonly double[] LineBuckets = [0, 1, 2, 5, 10, 20, 50, 100];

    private readonly Histogram<double> _render;
    private readonly Histogram<double> _store;
    private readonly Histogram<long> _lines;

    public ManifestWorkerMetrics(Meter meter)
    {
        var duration = new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBucketsSeconds };

        _render = meter.CreateHistogram(
            "freedom.manifest.document.render.duration", "s", "How long rendering a manifest document took.",
            tags: null, advice: duration);
        _store = meter.CreateHistogram(
            "freedom.manifest.document.store.duration", "s", "How long writing a manifest document to blob storage took, by whether it worked.",
            tags: null, advice: duration);
        _lines = meter.CreateHistogram(
            "freedom.manifest.document.lines", "{line}", "How many boxes each rendered document listed.",
            tags: null,
            advice: new InstrumentAdvice<long> { HistogramBucketBoundaries = [.. LineBuckets.Select(bucket => (long)bucket)] });
    }

    public static ManifestWorkerMetrics Create(IMeterFactory meterFactory) => new(meterFactory.Create(MeterName));

    /// <summary>For processors built without a host: a real instance nothing listens to.</summary>
    public static ManifestWorkerMetrics Unobserved { get; } = new(new Meter($"{MeterName}.Unobserved"));

    public void Rendered(TimeSpan elapsed, int lines)
    {
        _render.Record(elapsed.TotalSeconds);
        _lines.Record(lines);
    }

    public void Stored(TimeSpan elapsed, bool succeeded) =>
        _store.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("result", succeeded ? "ok" : "error"));
}
