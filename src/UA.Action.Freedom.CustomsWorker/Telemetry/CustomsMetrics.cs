using System.Diagnostics;
using System.Diagnostics.Metrics;
using HMRC.GVMS;

namespace UA.Action.Freedom.CustomsWorker.Telemetry;

/// <summary>
/// What the Customs Worker's dealings with HMRC look like from outside: how long a submission
/// took and how it ended, why messages were poisoned, and what state HMRC reports for a GMR.
/// </summary>
/// <remarks>
/// Every tag is a bounded set — a fixed outcome label, a fixed reason, an HTTP status, the
/// <see cref="State"/> enum. Never a manifest, GMR, box or notification id: those are per-record
/// values that belong on a span or in a log, and they would make every series unique.
/// </remarks>
public sealed class CustomsMetrics
{
    public const string MeterName = "UA.Action.Freedom.CustomsWorker";

    /// <summary>The source outcome-poll spans are started on.</summary>
    public const string SourceName = MeterName;

    public static readonly ActivitySource Source = new(SourceName);

    // The HTTP client's timeout is 100s and the queue's visibility timeout 120s, so this covers
    // the whole range a submission can take.
    private static readonly double[] SubmissionBucketsSeconds = [0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120];

    private readonly Histogram<double> _submissionDuration;
    private readonly Counter<long> _deadLetters;
    private readonly Counter<long> _notifications;

    public CustomsMetrics(Meter meter)
    {
        _submissionDuration = meter.CreateHistogram(
            "freedom.gmr.submission.duration", "s", "How long submitting a goods movement record to HMRC took, by how it ended.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = SubmissionBucketsSeconds });
        _deadLetters = meter.CreateCounter<long>(
            "freedom.gmr.dead_letters", "{message}", "Submissions moved to the poison queue, by why.");
        _notifications = meter.CreateCounter<long>(
            "freedom.gmr.outcome.notifications", "{notification}", "Notifications collected from HMRC's box, by what was done with them and the GMR state they reported.");
    }

    public static CustomsMetrics Create(IMeterFactory meterFactory) => new(meterFactory.Create(MeterName));

    /// <summary>For processors built without a host: a real instance nothing listens to.</summary>
    public static CustomsMetrics Unobserved { get; } = new(new Meter($"{MeterName}.Unobserved"));

    /// <param name="outcome"><c>accepted</c>, <c>rejected</c> (a 4xx) or <c>error</c> (anything else).</param>
    public void SubmissionCompleted(string outcome, TimeSpan elapsed) =>
        _submissionDuration.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("outcome", outcome));

    /// <param name="reason"><c>unreadable</c>, <c>no_manifest_ref</c> or <c>hmrc_rejected</c>.</param>
    /// <param name="httpStatus">HMRC's status, for <c>hmrc_rejected</c> only — it separates a bad
    /// submission (400, 422) from a credential problem (401, 403) or a throttle (429).</param>
    public void DeadLettered(string reason, int? httpStatus)
    {
        if (httpStatus is { } status)
        {
            _deadLetters.Add(
                1,
                new KeyValuePair<string, object?>("reason", reason),
                new KeyValuePair<string, object?>("http.response.status_code", status));
            return;
        }

        _deadLetters.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }

    /// <param name="result"><c>stored</c>, <c>unreadable</c>, <c>no_gmr_id</c> or <c>store_failed</c>.</param>
    /// <param name="reportedState">The <c>state</c> field HMRC sent. Anything that is not a known
    /// <see cref="State"/> is reported as <c>unknown</c>, so a surprising payload cannot mint labels.</param>
    public void OutcomeCollected(string result, string? reportedState) =>
        _notifications.Add(
            1,
            new KeyValuePair<string, object?>("result", result),
            new KeyValuePair<string, object?>("state", StateLabel(reportedState)));

    private static string StateLabel(string? reportedState) =>
        reportedState is null
            ? "none"
            : Enum.TryParse<State>(reportedState, ignoreCase: false, out var state) && Enum.IsDefined(state)
                ? state.ToString()
                : "unknown";
}
