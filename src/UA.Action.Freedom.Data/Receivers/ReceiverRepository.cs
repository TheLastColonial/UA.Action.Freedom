using Dapper;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Data.Receivers;

/// <summary>
/// Dapper-backed <see cref="IReceiverRepository"/> over <c>dbo.Receiver</c>, using the
/// application's own database identity.
/// </summary>
/// <remarks>
/// Every statement here names <c>dbo.Receiver</c> and nothing else. That identity is
/// <c>DENY SELECT</c>'d on the <c>sensitive</c> schema, so a query added here that reached for
/// an address would fail at the database rather than quietly succeed.
/// </remarks>
public sealed class ReceiverRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution) : IReceiverRepository
{
    private static readonly string Columns =
        $"r.ReceiverRef AS [Ref], r.Organisation, r.Region, r.[Status], {ChangeStamp.ReadColumns("r")}";

    private static readonly string From = $"dbo.Receiver AS r {ChangeStamp.ReadJoin("r")}";

    public async Task<ReceiverReadModel?> GetByRefAsync(Guid receiverRef, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<ReceiverReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM {From} WHERE r.ReceiverRef = @receiverRef",
            new { receiverRef },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ReceiverReadModel>> ListAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<ReceiverReadModel>(new CommandDefinition(
            $"""
             SELECT {Columns} FROM {From}
             ORDER BY r.Organisation, r.ReceiverRef
             OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
             """,
            new { skip = (page - 1) * pageSize, take = pageSize },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<bool> ExistsAsync(Guid receiverRef, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.Receiver WHERE ReceiverRef = @receiverRef",
            new { receiverRef },
            cancellationToken: cancellationToken));

        return count > 0;
    }

    public async Task AddAsync(ReceiverReadModel receiver, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.Receiver (ReceiverRef, Organisation, Region, LastChangedBy, LastChangedAt)
            VALUES (@Ref, @Organisation, @Region, @changedBy, SYSUTCDATETIME())
            """,
            attribution.With(receiver),
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(ReceiverReadModel receiver, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Receiver SET
                Organisation = @Organisation,
                Region = @Region,
                UpdatedAt = SYSUTCDATETIME(),
                LastChangedBy = @changedBy,
                LastChangedAt = SYSUTCDATETIME()
            WHERE ReceiverRef = @Ref
            """,
            attribution.With(receiver),
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> DeleteAsync(Guid receiverRef, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // The foreign key from sensitive.ReceiverDetail refuses this while detail still exists,
        // which is deliberate: it makes "delete the reference, keep the address" impossible.
        // DeleteReceiverHandler clears the detail through the Ground Officer identity first.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.Receiver WHERE ReceiverRef = @receiverRef",
            new { receiverRef },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> SetStatusAsync(Guid receiverRef, ReceiverStatus status, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // The only statement that writes Status: UpdateAsync leaves it out, so an ordinary edit
        // cannot register a receiver.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Receiver SET
                [Status] = @status,
                UpdatedAt = SYSUTCDATETIME(),
                LastChangedBy = @changedBy,
                LastChangedAt = SYSUTCDATETIME()
            WHERE ReceiverRef = @receiverRef
            """,
            attribution.With(new { receiverRef, status = (int)status }),
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<ReceiverUsageReadModel> GetUsageAsync(Guid receiverRef, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Identifiers only. A convoy is touched when one of its vehicles is handed over to the receiver,
        // or a manifest on it carries a box addressed to the receiver; an arrived convoy is history.
        var boxes = await connection.QueryAsync<int>(new CommandDefinition(
            "SELECT Id FROM dbo.Box WHERE ReceiverRef = @receiverRef ORDER BY Id",
            new { receiverRef },
            cancellationToken: cancellationToken));

        var convoys = await connection.QueryAsync<int>(new CommandDefinition(
            """
            SELECT touched.ConvoyId
            FROM (
                SELECT cv.ConvoyId FROM dbo.ConvoyVehicle AS cv WHERE cv.HandoverReceiverRef = @receiverRef
                UNION
                SELECT a.ConvoyId
                FROM dbo.Box AS b
                INNER JOIN dbo.ConvoyVehicleBoxAllocation AS a ON a.BoxId = b.Id
                WHERE b.ReceiverRef = @receiverRef
            ) AS touched
            INNER JOIN dbo.Convoy AS c ON c.Id = touched.ConvoyId
            WHERE c.ArrivedAt IS NULL
            ORDER BY touched.ConvoyId
            """,
            new { receiverRef },
            cancellationToken: cancellationToken));

        return new ReceiverUsageReadModel(boxes.ToList(), convoys.ToList());
    }
}
