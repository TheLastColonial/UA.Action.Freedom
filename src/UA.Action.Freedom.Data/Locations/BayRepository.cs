using Dapper;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Locations;

namespace UA.Action.Freedom.Data.Locations;

/// <summary>
/// Dapper-backed <see cref="IBayRepository"/> over <c>dbo.Bay</c>.
/// </summary>
public sealed class BayRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution) : IBayRepository
{
    private const string Columns = "Id, LocationId, Code";

    public async Task<BayReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<BayReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM dbo.Bay WHERE Id = @id",
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<BayReadModel>> ListByLocationAsync(int locationId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<BayReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM dbo.Bay WHERE LocationId = @locationId ORDER BY Code",
            new { locationId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<bool> CodeExistsAsync(int locationId, string code, int? excludeId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1) FROM dbo.Bay
            WHERE LocationId = @locationId AND Code = @code AND (@excludeId IS NULL OR Id <> @excludeId)
            """,
            new { locationId, code, excludeId },
            cancellationToken: cancellationToken));

        return count > 0;
    }

    public async Task<int> AddAsync(BayReadModel bay, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO dbo.Bay (LocationId, Code, LastChangedBy, LastChangedAt)
            VALUES (@LocationId, @Code, @changedBy, SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """,
            attribution.With(bay),
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(BayReadModel bay, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Bay SET
                LocationId = @LocationId,
                Code = @Code,
                LastChangedBy = @changedBy,
                LastChangedAt = SYSUTCDATETIME()
            WHERE Id = @Id
            """,
            attribution.With(bay),
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.Bay WHERE Id = @id",
            new { id },
            cancellationToken: cancellationToken));

        return affected > 0;
    }
}
