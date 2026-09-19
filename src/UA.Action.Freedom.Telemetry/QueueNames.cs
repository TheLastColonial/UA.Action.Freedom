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
}
