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
