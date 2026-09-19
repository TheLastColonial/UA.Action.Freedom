using System.Diagnostics.Metrics;
using Microsoft.Data.SqlClient;

namespace UA.Action.Freedom.Data;

/// <summary>
/// Counts the SQL errors that were not anticipated — the deadlocks, timeouts and unreachable
/// servers that reach the API as a 500 — under a small fixed set of classes.
/// </summary>
/// <remarks>
/// The errors the repositories <em>do</em> anticipate (a foreign key refusing a delete, a lost
/// race on a unique seat) are business outcomes and are counted as handler outcomes already; this
/// is only for what nobody planned for. The tag is the class, never the error number or message:
/// a SQL message can name a table, a login or a value.
/// </remarks>
public static class DbErrors
{
    public const string MeterName = "UA.Action.Freedom.Data";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Errors = Meter.CreateCounter<long>(
        "freedom.db.errors", "{error}", "SQL errors that reached the API unhandled, by class.");

    /// <summary>
    /// Azure SQL waking from auto-pause answers 40613 for a while; the rest are the usual "cannot
    /// reach the server" numbers.
    /// </summary>
    private static readonly HashSet<int> Unavailable = [-1, 2, 53, 233, 40197, 40501, 40613];

    public static string Classify(int number) => number switch
    {
        SqlErrors.ForeignKeyViolation => "foreign_key",
        2627 or 2601 => "unique",
        1205 => "deadlock",
        -2 => "timeout",
        _ when Unavailable.Contains(number) => "unavailable",
        _ => "other",
    };

    public static void Record(int number) =>
        Errors.Add(1, new KeyValuePair<string, object?>("sql_error", Classify(number)));

    /// <summary>Counts <paramref name="failure"/> if it is, or wraps, a <see cref="SqlException"/>; ignores anything else.</summary>
    public static void Record(Exception? failure)
    {
        var sql = failure as SqlException ?? failure?.InnerException as SqlException;

        if (sql is not null)
        {
            Record(sql.Number);
        }
    }
}
