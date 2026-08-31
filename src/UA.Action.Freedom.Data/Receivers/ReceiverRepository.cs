using Dapper;
using UA.Action.Freedom.Application.Receivers;

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
public sealed class ReceiverRepository(IDbConnectionFactory connectionFactory) : IReceiverRepository
{
    private const string Columns = "ReceiverRef AS [Ref], Organisation, Region";

    public async Task<ReceiverReadModel?> GetByRefAsync(Guid receiverRef, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<ReceiverReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM dbo.Receiver WHERE ReceiverRef = @receiverRef",
            new { receiverRef },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ReceiverReadModel>> ListAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<ReceiverReadModel>(new CommandDefinition(
            $"""
             SELECT {Columns} FROM dbo.Receiver
             ORDER BY Organisation, ReceiverRef
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
            INSERT INTO dbo.Receiver (ReceiverRef, Organisation, Region)
            VALUES (@Ref, @Organisation, @Region)
            """,
            receiver,
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
                UpdatedAt = SYSUTCDATETIME()
            WHERE ReceiverRef = @Ref
            """,
            receiver,
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
}
