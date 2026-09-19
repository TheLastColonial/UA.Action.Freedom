using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry;

namespace UA.Action.Freedom.Telemetry;

/// <summary>
/// Strips from every span the parts of a URL that identify a person, a receiver, a vehicle or a
/// capability, so that traces are safe to keep whatever the instrumentation happened to record.
/// </summary>
/// <remarks>
/// <para>
/// <b>Server spans</b> keep the route template (<c>/boxes/scan/{token}</c>), never the concrete
/// path (<c>/boxes/scan/9f1c…</c>): the token, a VIN, a person id or a receiver reference in the
/// path is exactly what a trace must not become an index of. The query string goes entirely —
/// <c>GET /receivers/{ref}/detail?reason=…</c> takes free text, and the instrumentation redacts
/// only query keys it knows to be secret.
/// </para>
/// <para>
/// <b>Client spans</b> keep the peer (<c>scheme://host:port</c>) and lose the path, which carries
/// HMRC's notification-box id and GMR ids and the storage account's queue and blob names.
/// <c>server.address</c> still groups them by peer.
/// </para>
/// <para>
/// This runs in <see cref="OnEnd"/>, when the instrumentation has finished writing tags
/// (<c>http.route</c> is only known once routing has run). It must be registered before the
/// exporter so the exporter sees the redacted span.
/// </para>
/// </remarks>
public sealed class RedactingActivityProcessor : BaseProcessor<Activity>
{
    private const string Unmatched = "(unmatched)";

    private const string SensitiveStatementPlaceholder = "(statement on the sensitive schema not recorded)";

    private static readonly string[] ClientUrlTags = ["url.full", "http.url"];

    /// <summary>Everything the SQL instrumentation derives from the statement, across semantic-convention versions.</summary>
    private static readonly string[] StatementTags =
        ["db.query.text", "db.statement", "db.query.summary", "db.collection.name", "db.sql.table", "db.operation", "db.operation.name"];

    /// <summary><c>sensitive.X</c>, <c>[sensitive].[X]</c>, in any case.</summary>
    private static readonly Regex SensitiveSchema =
        new(@"(?<![\w])\[?sensitive\]?\s*\.", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    public override void OnEnd(Activity data)
    {
        switch (data.Kind)
        {
            case ActivityKind.Server:
                RedactServer(data);
                break;
            case ActivityKind.Client:
                RedactClient(data);
                break;
        }
    }

    private static void RedactServer(Activity activity)
    {
        if (activity.GetTagItem("url.path") is not null)
        {
            var route = activity.GetTagItem("http.route") as string;
            activity.SetTag("url.path", string.IsNullOrEmpty(route) ? Unmatched : route);
        }

        activity.SetTag("url.query", null);
    }

    private static void RedactClient(Activity activity)
    {
        foreach (var tag in ClientUrlTags)
        {
            if (activity.GetTagItem(tag) is string url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                activity.SetTag(tag, uri.GetLeftPart(UriPartial.Authority));
            }
        }

        RedactSensitiveSchemaStatement(activity);
    }

    /// <summary>
    /// SQL statements are recorded — a slow query is found by its text — except those on the
    /// <c>sensitive</c> schema. They are parameterised, so they hold no values, but their column
    /// names describe the shape of the delivery-address data.
    /// </summary>
    private static void RedactSensitiveSchemaStatement(Activity activity)
    {
        var statement = activity.GetTagItem("db.query.text") as string
                        ?? activity.GetTagItem("db.statement") as string;

        if (statement is null || !SensitiveSchema.IsMatch(statement))
        {
            return;
        }

        foreach (var tag in StatementTags)
        {
            if (activity.GetTagItem(tag) is not null)
            {
                activity.SetTag(tag, null);
            }
        }

        activity.SetTag("db.query.text", SensitiveStatementPlaceholder);
        activity.DisplayName = "SQL (sensitive schema)";
    }
}
