using Dapper;
using Microsoft.Data.SqlClient;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Locations;

namespace UA.Action.Freedom.Data.Locations;

/// <summary>
/// Dapper-backed <see cref="ILoaderAssignmentRepository"/> over <c>dbo.LoaderLocationAssignment</c>. Only open rows
/// (<c>Until IS NULL</c>) ever authorise anything.
/// </summary>
public sealed class LoaderAssignmentRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution)
    : ILoaderAssignmentRepository
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public async Task<IReadOnlyList<LoaderAssignmentReadModel>> HistoryAsync(
        int locationId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<LoaderAssignmentReadModel>(new CommandDefinition(
            $"""
             SELECT a.Id, a.LocationId, a.PersonId, loader.DisplayName AS PersonName, a.[From], a.Until,
                    {ChangeStamp.ReadColumns("a")}
             FROM dbo.LoaderLocationAssignment AS a
             JOIN dbo.PersonDisplay AS loader ON loader.PersonId = a.PersonId
             {ChangeStamp.ReadJoin("a")}
             WHERE a.LocationId = @locationId
             ORDER BY a.[From] DESC, a.Id DESC
             """,
            new { locationId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<AssignLoaderResult> AssignAsync(
        int locationId, Guid personId, DateTime at, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        try
        {
            var inserted = await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.LoaderLocationAssignment (LocationId, PersonId, [From], LastChangedBy, LastChangedAt)
                SELECT @locationId, @personId, @at, @changedBy, SYSUTCDATETIME()
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.LoaderLocationAssignment
                    WHERE LocationId = @locationId AND PersonId = @personId AND Until IS NULL)
                """,
                attribution.With(new { locationId, personId, at }),
                cancellationToken: cancellationToken));

            return inserted > 0 ? AssignLoaderResult.Assigned : AssignLoaderResult.AlreadyAssigned;
        }
        catch (SqlException exception) when (exception.Number is UniqueIndexViolation or UniqueConstraintViolation)
        {
            // Two assignments raced: the filtered unique index settled it.
            return AssignLoaderResult.AlreadyAssigned;
        }
    }

    public async Task<bool> UnassignAsync(int locationId, Guid personId, DateTime at, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var closed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.LoaderLocationAssignment SET
                Until = @at, LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
            WHERE LocationId = @locationId AND PersonId = @personId AND Until IS NULL
            """,
            attribution.With(new { locationId, personId, at }),
            cancellationToken: cancellationToken));

        return closed > 0;
    }

    public async Task<bool> ManagesAsync(Guid personId, int locationId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM dbo.LoaderLocationAssignment
                WHERE PersonId = @personId AND LocationId = @locationId AND Until IS NULL) THEN 1 ELSE 0 END AS bit)
            """,
            new { personId, locationId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<int>> ManagedLocationIdsAsync(Guid personId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var ids = await connection.QueryAsync<int>(new CommandDefinition(
            "SELECT LocationId FROM dbo.LoaderLocationAssignment WHERE PersonId = @personId AND Until IS NULL ORDER BY LocationId",
            new { personId },
            cancellationToken: cancellationToken));

        return ids.ToList();
    }
}
