namespace UA.Action.Freedom.Telemetry;

/// <summary>
/// The logical names queues carry as a metric label. Deliberately not the configured storage
/// queue names: those are per-environment, and a dashboard should not have to know which
/// environment it is looking at.
/// </summary>
public static class QueueNames
{
    public const string CustomsWork = "customs-work";

    public const string ManifestDocuments = "manifest-documents";

    /// <summary>
    /// French customs logistics envelopes. Its own queue rather than a message type on
    /// <see cref="CustomsWork"/>: a different border, a different authority and a different failure
    /// mode, so it deserves its own depth, age and dead-letter series.
    /// </summary>
    public const string EloEnvelopes = "elo-envelopes";
}
