using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using UA.Action.Freedom.Data;

namespace UA.Action.Freedom.Tests.Integration;

/// <summary>
/// The one database every repository test runs against, and the raw-SQL helpers they use to
/// arrange rows the repository under test does not own.
/// </summary>
/// <remarks>
/// <para>
/// Connects as <c>freedom_app</c> — the identity the application itself uses — not <c>sa</c>.
/// A sysadmin bypasses every permission check, so a test run as <c>sa</c> can pass against a
/// grant the application does not actually hold. <c>ConnectionStrings__Freedom</c> overrides it.
/// </para>
/// <para>
/// When the database is not reachable a test skips, so <c>dotnet test</c> stays green on a
/// machine with no stack up. Set <c>FREEDOM_REQUIRE_INTEGRATION=true</c> (CI's acceptance job
/// does, having just stood the stack up) and the same condition fails the test instead: a skipped
/// integration suite in that job means the infrastructure is broken, not absent.
/// </para>
/// </remarks>
internal static class SqlTestDatabase
{
    private const string DefaultLocalConnectionString =
        "Server=localhost,1433;Database=Freedom;User Id=freedom_app;Password=Local_Freedom_App_1;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3";

    internal static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Freedom") ?? DefaultLocalConnectionString;

    private static bool Required =>
        string.Equals(Environment.GetEnvironmentVariable("FREEDOM_REQUIRE_INTEGRATION"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs <paramref name="probeSql"/> — typically a <c>SELECT COUNT(1)</c> over the tables the
    /// test needs — and skips (or fails, when required) if it cannot, so a database that predates
    /// a table reads as "not set up" rather than as a failing repository.
    /// </summary>
    internal static async Task SkipUnlessReachableAsync(string probeSql, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = probeSql;
            await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception exception) when (!Required)
        {
            Assert.Skip($"Freedom database is not reachable or not provisioned ({probeSql}): {exception.Message}");
        }
    }

    internal static SqlConnectionFactory ConnectionFactory() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Freedom"] = ConnectionString })
            .Build());

    internal static Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters) =>
        RunAsync(sql, parameters, command => command.ExecuteNonQueryAsync());

    internal static async Task<int> ScalarAsync(string sql, params (string Name, object Value)[] parameters) =>
        Convert.ToInt32(await ValueAsync(sql, parameters));

    internal static Task<object?> ValueAsync(string sql, params (string Name, object Value)[] parameters) =>
        RunAsync(sql, parameters, command => command.ExecuteScalarAsync());

    /// <summary>
    /// A volunteer on file: the anonymous <c>dbo.Person</c> identity every foreign key points at,
    /// plus the <c>dbo.PersonDetail</c> row holding their personal data. Removing the person row
    /// cascades the detail.
    /// </summary>
    internal static async Task<Guid> AddVolunteerAsync(
        string firstName = "Integration", string lastName = "Volunteer", bool isDriver = true)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT INTO dbo.Person (Id) VALUES (@id);
            INSERT INTO dbo.PersonDetail (PersonId, FirstName, LastName, DateOfBirth, Joined, IsDriver)
            VALUES (@id, @firstName, @lastName, '1985-01-01', '2024-01-01', @isDriver);
            """,
            ("@id", id),
            ("@firstName", firstName),
            ("@lastName", lastName),
            ("@isDriver", isDriver));
        return id;
    }

    private static async Task<T> RunAsync<T>(
        string sql, (string Name, object Value)[] parameters, Func<SqlCommand, Task<T>> run)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await run(command);
    }
}
