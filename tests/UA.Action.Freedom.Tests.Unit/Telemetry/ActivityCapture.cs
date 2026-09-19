using System.Diagnostics;

namespace UA.Action.Freedom.Tests.Unit.Telemetry;

/// <summary>Records every activity started on the named sources while it is alive.</summary>
public sealed class ActivityCapture : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _stopped = [];
    private readonly object _gate = new();

    public ActivityCapture(params string[] sourceNames)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => sourceNames.Contains(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_gate)
                {
                    _stopped.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<Activity> Stopped
    {
        get
        {
            lock (_gate)
            {
                return [.. _stopped];
            }
        }
    }

    /// <summary>Activities carrying <paramref name="key"/>=<paramref name="value"/>, so parallel tests can find their own.</summary>
    public IReadOnlyList<Activity> WithTag(string key, string value) =>
        [.. Stopped.Where(a => Equals(a.GetTagItem(key), value))];

    public void Dispose() => _listener.Dispose();
}
