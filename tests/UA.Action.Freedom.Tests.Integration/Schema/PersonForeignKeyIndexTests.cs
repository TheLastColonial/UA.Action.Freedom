using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Schema;

/// <summary>
/// Every foreign key to <c>dbo.Person</c> is indexed on the referencing column. Every erasure runs
/// <c>DELETE FROM dbo.Person</c>, and SQL Server checks each of those keys by scanning the child table; an
/// unindexed one scans under lock, and that scan deadlocked the BDD suite's erasures against concurrent writes.
/// </summary>
/// <remarks>
/// Reads the catalogue as the Ground Officer's identity, as <see cref="LastChangedGuardTests"/> does, so the
/// <c>sensitive</c> schema is visible too.
/// </remarks>
[Trait("Category", "Integration")]
public class PersonForeignKeyIndexTests
{
    [Fact]
    public async Task Every_foreign_key_to_person_is_indexed_on_its_referencing_column()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SkipUnlessReachableAsync("SELECT COUNT(1) FROM sys.foreign_keys", cancellationToken);

        var unindexed = await UnindexedPersonKeysAsync();

        unindexed.Should().BeEmpty(
            "an unindexed foreign key to dbo.Person scans its child table on every erasure; index the column " +
            "(filtered on IS NOT NULL when it is nullable, as the other LastChangedBy indexes are)");
    }

    private static async Task<IReadOnlyList<string>> UnindexedPersonKeysAsync()
    {
        await using var connection = new SqlConnection(SensitiveConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT OBJECT_SCHEMA_NAME(fk.parent_object_id) + '.' + OBJECT_NAME(fk.parent_object_id) + '.'
                   + COL_NAME(fkc.parent_object_id, fkc.parent_column_id)
            FROM sys.foreign_keys AS fk
            JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
            WHERE fk.referenced_object_id = OBJECT_ID('dbo.Person')
              AND NOT EXISTS (
                  SELECT 1
                  FROM sys.index_columns AS ic
                  WHERE ic.object_id = fk.parent_object_id
                    AND ic.column_id = fkc.parent_column_id
                    AND ic.key_ordinal = 1)
            ORDER BY 1
            """;

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
