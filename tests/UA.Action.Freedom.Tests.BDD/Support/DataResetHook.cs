using System.Data;
using Microsoft.Data.SqlClient;
using Reqnroll;

namespace UA.Action.Freedom.Tests.BDD.Support;

/// <summary>
/// Clears the rows a BDD run created from the database once the whole suite has finished.
/// </summary>
/// <remarks>
/// The per-scenario HTTP cleanup in <see cref="CleanupHooks"/> is the fast path, but it cannot
/// remove everything: once a manifest is approved it is frozen (<c>GmrSubmittedAt</c> set) and
/// <c>DELETE /manifests/{id}</c> returns 409, which also pins the convoy it names. Those rows
/// otherwise accumulate in the shared database until <c>docker compose down -v</c>.
///
/// This hook takes a database timestamp before the suite runs and deletes every row created at
/// or after it once the suite ends — scoped to this run, so a developer's hand-made rows from
/// before the run are left alone. It connects as <c>sa</c> (the only identity that may delete
/// from <c>sensitive.*</c>, mirroring <c>iac/local/sql/002-reset-data.sql</c>) and is strictly
/// best effort: any failure is logged and swallowed so a teardown problem never fails the run.
/// Set <c>FREEDOM_SKIP_DB_RESET</c> when pointing the suite at a remote target where the local
/// SQL Server is not reachable.
/// </remarks>
[Binding]
public static class DataResetHook
{
    private const string DefaultConnectionString =
        "Server=localhost,1433;Database=Freedom;User Id=sa;Password=Local_Freedom_Dev_1;"
        + "TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    // Same tables and order as iac/local/sql/003-clean-since.sql. Children without a CreatedAt
    // column ride ON DELETE CASCADE from their parent; the NO ACTION foreign keys
    // (ReceiverDetail -> Receiver, Box -> Receiver/Person, Manifest -> Vehicle/Convoy) are what
    // make the order load-bearing.
    private const string CleanupSql = """
        DELETE FROM sensitive.ReceiverDetailAccessLog WHERE ReadAt >= @mark;
        DELETE FROM sensitive.ReceiverDetail
        WHERE ReceiverRef IN (SELECT ReceiverRef FROM dbo.Receiver WHERE CreatedAt >= @mark);
        DELETE FROM dbo.Manifest WHERE CreatedAt >= @mark;
        DELETE FROM dbo.Box      WHERE CreatedAt >= @mark;
        DELETE FROM dbo.Vehicle  WHERE CreatedAt >= @mark;
        DELETE FROM dbo.Convoy   WHERE CreatedAt >= @mark;
        DELETE FROM dbo.Person   WHERE CreatedAt >= @mark;
        DELETE FROM dbo.Receiver WHERE CreatedAt >= @mark;
        """;

    private static DateTime? runStartedAt;

    private static bool Skipped =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FREEDOM_SKIP_DB_RESET"));

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Freedom") ?? DefaultConnectionString;

    [BeforeTestRun]
    public static async Task RecordRunStart()
    {
        if (Skipped)
        {
            return;
        }

        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT SYSUTCDATETIME();";
            runStartedAt = (DateTime)(await command.ExecuteScalarAsync())!;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[BDD teardown] could not read a start marker, data reset disabled: {exception.Message}");
        }
    }

    [AfterTestRun]
    public static async Task ResetDataCreatedByTheRun()
    {
        if (Skipped || runStartedAt is not { } mark)
        {
            return;
        }

        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = CleanupSql;
            command.Parameters.Add(new SqlParameter("@mark", SqlDbType.DateTime2) { Value = mark });
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[BDD teardown] data reset skipped: {exception.Message}");
        }
    }
}
