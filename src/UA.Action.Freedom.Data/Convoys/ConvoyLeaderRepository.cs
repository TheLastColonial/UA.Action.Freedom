using Dapper;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Data.Convoys;

/// <summary>
/// Dapper-backed <see cref="IConvoyLeaderRepository"/> over <c>dbo.ConvoyLeaderAssignment</c>: who leads a convoy, and
/// who led it before.
/// </summary>
public sealed class ConvoyLeaderRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution)
    : IConvoyLeaderRepository
{
    public async Task<IReadOnlyList<ConvoyLeaderAssignmentReadModel>> HistoryAsync(
        int convoyId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<ConvoyLeaderAssignmentReadModel>(new CommandDefinition(
            $"""
             SELECT a.Id, a.ConvoyId, a.PersonId, leader.DisplayName AS PersonName, a.[From], a.Until,
                    {ChangeStamp.ReadColumns("a")}
             FROM dbo.ConvoyLeaderAssignment AS a
             JOIN dbo.PersonDisplay AS leader ON leader.PersonId = a.PersonId
             {ChangeStamp.ReadJoin("a")}
             WHERE a.ConvoyId = @convoyId
             ORDER BY a.[From] DESC, a.Id DESC
             """,
            new { convoyId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<bool> IsCurrentLeaderAsync(int convoyId, Guid personId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM dbo.ConvoyLeaderAssignment
                WHERE ConvoyId = @convoyId AND PersonId = @personId AND Until IS NULL) THEN 1 ELSE 0 END AS bit)
            """,
            new { convoyId, personId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<int>> LedConvoyIdsAsync(Guid personId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var ids = await connection.QueryAsync<int>(new CommandDefinition(
            "SELECT ConvoyId FROM dbo.ConvoyLeaderAssignment WHERE PersonId = @personId AND Until IS NULL ORDER BY ConvoyId",
            new { personId },
            cancellationToken: cancellationToken));

        return ids.ToList();
    }

    public async Task<NominateLeaderResult> NominateAsync(
        int convoyId, Guid personId, DateTime at, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        // Closing the old assignment and opening the new one is one fact: a failure between them would leave the
        // convoy with no leader, or the filtered unique index refusing the second open row.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var alreadyLeader = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM dbo.ConvoyLeaderAssignment WITH (UPDLOCK, HOLDLOCK)
                WHERE ConvoyId = @convoyId AND PersonId = @personId AND Until IS NULL) THEN 1 ELSE 0 END AS bit)
            """,
            new { convoyId, personId },
            transaction,
            cancellationToken: cancellationToken));

        if (alreadyLeader)
        {
            return NominateLeaderResult.AlreadyLeader;
        }

        var isDriver = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            $"""
             SELECT CAST(CASE WHEN EXISTS (
                 SELECT 1 FROM dbo.ConvoyVehicleCrew WITH (UPDLOCK)
                 WHERE ConvoyId = @convoyId AND PersonId = @personId AND [Role] = {(int)CrewRole.Driver}) THEN 1 ELSE 0 END AS bit)
             """,
            new { convoyId, personId },
            transaction,
            cancellationToken: cancellationToken));

        if (!isDriver)
        {
            return NominateLeaderResult.NotADriverOnConvoy;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ConvoyLeaderAssignment SET
                Until = @at, LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
            WHERE ConvoyId = @convoyId AND Until IS NULL
            """,
            attribution.With(new { convoyId, at }),
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.ConvoyLeaderAssignment (ConvoyId, PersonId, [From], LastChangedBy, LastChangedAt)
            VALUES (@convoyId, @personId, @at, @changedBy, SYSUTCDATETIME())
            """,
            attribution.With(new { convoyId, personId, at }),
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return NominateLeaderResult.Nominated;
    }
}
