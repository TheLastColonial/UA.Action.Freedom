using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Schema;

/// <summary>
/// The rule of ADR 0017, enforced against the real schema: every entity table records who last
/// changed a row and when. A table added later without the two columns fails here, so the rule
/// does not depend on anyone remembering it.
/// </summary>
/// <remarks>
/// Reads the catalogue as the Ground Officer's identity, not <c>freedom_app</c>: the application is
/// denied the <c>sensitive</c> schema, and that denial hides its tables from the catalogue too, so
/// the guard would be blind to exactly the tables that hold the most sensitive data.
/// </remarks>
[Trait("Category", "Integration")]
public class LastChangedGuardTests
{
    private static readonly IReadOnlyDictionary<string, string> Exempt = new Dictionary<string, string>
    {
        ["dbo.Person"] = "the anonymous key; a change to a volunteer is recorded on dbo.PersonDetail, which erasure deletes",
        ["dbo.Donor"] = "the anonymous key; a change to a donor is recorded on dbo.DonorDetail, which erasure deletes",
        ["dbo.ManifestBox"] = "a link between a manifest and a box, never updated",
        ["dbo.ConvoyVehicleInsuranceDriver"] = "a link between an insurance and a driver, never updated",
        ["dbo.ConvoyVehicleInsurance"] = "RecordedByPersonId and RecordedAt already say who and when",
        ["dbo.BoxBayAssignment"] = "AssignedByPersonId and AssignedAt already say who and when",
        ["sensitive.ReceiverDetailAccessLog"] = "an append-only log whose PersonId is who",
    };

    [Fact]
    public async Task Every_entity_table_records_who_last_changed_it_and_when()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SkipUnlessReachableAsync("SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS", cancellationToken);

        var tables = await TablesWithoutBothColumnsAsync();

        tables.Except(Exempt.Keys).Should().BeEmpty(
            "every entity table needs LastChangedBy and LastChangedAt (ADR 0017); " +
            "a link table or an append-only log that already says who belongs in the exempt list with its reason");
    }

    [Fact]
    public async Task An_exempt_table_still_exists_so_the_list_cannot_go_stale()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SkipUnlessReachableAsync("SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS", cancellationToken);

        var existing = await TableNamesAsync();

        Exempt.Keys.Except(existing).Should().BeEmpty();
    }

    private static async Task<IReadOnlyList<string>> TableNamesAsync()
    {
        await using var connection = new SqlConnection(SensitiveConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT TABLE_SCHEMA + '.' + TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";
        return await ReadAsync(command);
    }

    private static async Task<IReadOnlyList<string>> TablesWithoutBothColumnsAsync()
    {
        await using var connection = new SqlConnection(SensitiveConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT t.TABLE_SCHEMA + '.' + t.TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES AS t
            WHERE t.TABLE_TYPE = 'BASE TABLE'
              AND (SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS AS c
                   WHERE c.TABLE_SCHEMA = t.TABLE_SCHEMA AND c.TABLE_NAME = t.TABLE_NAME
                     AND c.COLUMN_NAME IN ('LastChangedBy', 'LastChangedAt')) < 2
            """;
        return await ReadAsync(command);
    }

    private static async Task<IReadOnlyList<string>> ReadAsync(SqlCommand command)
    {
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
