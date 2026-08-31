namespace UA.Action.Freedom.ManifestWorker.Configuration;

/// <summary>How often the worker looks for work.</summary>
/// <remarks>
/// Convoys run about once a month, so this is idle almost all the time and then has a burst of
/// a few days (docs/domain/key-concepts.md § Operating Rhythm). The poll interval is what it
/// costs to be idle; the drain-while-there-is-work loop is what makes the burst quick.
/// </remarks>
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public int QueuePollSeconds { get; set; } = 15;
}
