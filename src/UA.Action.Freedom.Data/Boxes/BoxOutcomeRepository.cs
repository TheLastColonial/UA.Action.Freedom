using Dapper;
using Microsoft.Data.SqlClient;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Data.Boxes;

/// <summary>
/// Dapper-backed <see cref="IBoxOutcomeRepository"/> over <c>dbo.BoxOutcome</c>. A change of outcome is one
/// transaction: the old row stops being current, the new one is inserted, and a return to a hub also takes the
/// box off its vehicle and puts it at the hub. The database settles a race: the old row is cleared conditionally
/// on the outcome the caller saw, and the filtered unique index refuses a second current row.
/// </summary>
public sealed class BoxOutcomeRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution)
    : IBoxOutcomeRepository
{
    private const string Columns =
        "o.BoxId, o.ConvoyId, o.Vin, o.Outcome, o.Reason, o.HubLocationId, o.RecordedAt, who.DisplayName AS RecordedByName";

    private const string From =
        "dbo.BoxOutcome AS o LEFT JOIN dbo.PersonDisplay AS who ON who.PersonId = o.RecordedBy";

    public async Task<BoxOutcomeReadModel?> GetCurrentAsync(
        int convoyId, int boxId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<BoxOutcomeReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM {From} WHERE o.ConvoyId = @convoyId AND o.BoxId = @boxId AND o.IsCurrent = 1",
            new { convoyId, boxId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<BoxOutcomeReadModel>> ListCurrentAsync(
        int convoyId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<BoxOutcomeReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM {From} WHERE o.ConvoyId = @convoyId AND o.IsCurrent = 1 ORDER BY o.BoxId",
            new { convoyId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<int?> DeliverVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Write-once and conditional, so the database settles a race with a second Dispatcher.
        var stamped = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ConvoyVehicle
            SET DeliveredAt = SYSUTCDATETIME(), LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
            WHERE ConvoyId = @convoyId AND Vin = @vin AND DeliveredAt IS NULL AND WithdrawnAt IS NULL
            """,
            attribution.With(new { convoyId, vin = SqlKey.Of(vin) }),
            transaction,
            cancellationToken: cancellationToken));

        if (stamped == 0)
        {
            return null;
        }

        var delivered = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO dbo.BoxOutcome (BoxId, ConvoyId, Vin, Outcome, RecordedBy)
            SELECT a.BoxId, a.ConvoyId, a.Vin, @delivered, @changedBy
            FROM dbo.ConvoyVehicleBoxAllocation AS a
            INNER JOIN dbo.Box AS b ON b.Id = a.BoxId
            WHERE a.ConvoyId = @convoyId AND a.Vin = @vin AND b.VoidedAt IS NULL
              AND NOT EXISTS (SELECT 1 FROM dbo.BoxOutcome AS o
                              WHERE o.BoxId = a.BoxId AND o.ConvoyId = a.ConvoyId AND o.IsCurrent = 1);

            SELECT @@ROWCOUNT;
            """,
            attribution.With(new { convoyId, vin = SqlKey.Of(vin), delivered = (int)BoxOutcome.Delivered }),
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return delivered;
    }

    public async Task<int> AcceptGoodsListAsync(
        int convoyId, string vin, Guid receiverRef, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The old rows stop being current and the accepted ones are inserted in the same transaction, so a list is
        // accepted whole or not at all.
        var accepted = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            DECLARE @delivered TABLE (BoxId int NOT NULL);

            UPDATE o SET IsCurrent = 0
            OUTPUT deleted.BoxId INTO @delivered
            FROM dbo.BoxOutcome AS o
            INNER JOIN dbo.Box AS b ON b.Id = o.BoxId
            WHERE o.ConvoyId = @convoyId AND o.Vin = @vin AND o.IsCurrent = 1 AND o.Outcome = @delivered
              AND b.ReceiverRef = @receiverRef AND b.VoidedAt IS NULL;

            INSERT INTO dbo.BoxOutcome (BoxId, ConvoyId, Vin, Outcome, RecordedBy)
            SELECT BoxId, @convoyId, @vin, @accepted, @changedBy FROM @delivered;

            SELECT COUNT(1) FROM @delivered;
            """,
            attribution.With(new
            {
                convoyId,
                vin = SqlKey.Of(vin),
                receiverRef,
                delivered = (int)BoxOutcome.Delivered,
                accepted = (int)BoxOutcome.Accepted,
            }),
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return accepted;
    }

    public async Task<RecordBoxOutcomeResult> RecordAsync(
        RecordBoxOutcome outcome, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (outcome.From is { } seen)
            {
                var cleared = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.BoxOutcome SET IsCurrent = 0
                    WHERE BoxId = @boxId AND ConvoyId = @convoyId AND IsCurrent = 1 AND Outcome = @from
                    """,
                    new { boxId = outcome.BoxId, convoyId = outcome.ConvoyId, from = (int)seen },
                    transaction,
                    cancellationToken: cancellationToken));

                if (cleared == 0)
                {
                    return RecordBoxOutcomeResult.Conflict;
                }
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.BoxOutcome (BoxId, ConvoyId, Vin, Outcome, Reason, HubLocationId, RecordedBy)
                VALUES (@boxId, @convoyId, @vin, @outcome, @reason, @hubLocationId, @changedBy)
                """,
                attribution.With(new
                {
                    boxId = outcome.BoxId,
                    convoyId = outcome.ConvoyId,
                    vin = SqlKey.Of(outcome.Vin),
                    outcome = (int)outcome.Outcome,
                    reason = outcome.Reason,
                    hubLocationId = outcome.HubLocationId,
                }),
                transaction,
                cancellationToken: cancellationToken));

            if (outcome.Outcome == BoxOutcome.ReturnedToHub)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM dbo.ConvoyVehicleBoxAllocation WHERE BoxId = @boxId AND ConvoyId = @convoyId",
                    new { boxId = outcome.BoxId, convoyId = outcome.ConvoyId },
                    transaction,
                    cancellationToken: cancellationToken));

                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.Box
                    SET LocationId = @hubLocationId, LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
                    WHERE Id = @boxId
                    """,
                    attribution.With(new { boxId = outcome.BoxId, hubLocationId = outcome.HubLocationId }),
                    transaction,
                    cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            return RecordBoxOutcomeResult.Recorded;
        }
        catch (SqlException exception)
            when (exception.Number is SqlErrors.UniqueIndexViolation or SqlErrors.UniqueConstraintViolation)
        {
            // A second writer already holds the current row for this box on this convoy.
            return RecordBoxOutcomeResult.Conflict;
        }
    }
}
