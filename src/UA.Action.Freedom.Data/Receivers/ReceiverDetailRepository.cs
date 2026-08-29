using Dapper;
using UA.Action.Freedom.Application.Receivers;

namespace UA.Action.Freedom.Data.Receivers;

/// <summary>
/// Dapper-backed <see cref="IReceiverDetailRepository"/> over <c>sensitive.ReceiverDetail</c>,
/// using the Ground Officer database identity — the only one granted SELECT on that schema.
/// </summary>
/// <remarks>
/// This is the one class in the solution that can read a Ukrainian delivery address. It takes
/// <see cref="ISensitiveDbConnectionFactory"/> rather than <see cref="IDbConnectionFactory"/>,
/// so it cannot be constructed with the application's ordinary connection, and no other
/// repository can be constructed with this one.
/// </remarks>
public sealed class ReceiverDetailRepository(ISensitiveDbConnectionFactory connectionFactory)
    : IReceiverDetailRepository
{
    private const string Columns =
        "ReceiverRef AS [Ref], ContactName, ContactPhone, AddressLine1, AddressLine2, City, PostCode, DeleteAfter";

    public async Task<ReceiverDetailReadModel?> ResolveAsync(
        Guid receiverRef, string principalId, string? reason, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        // The audit row and the read commit together or not at all. Writing the log afterwards
        // would leave a window — a crash, a cancelled request, an exception on the way out — in
        // which an address was disclosed and nothing recorded it. §4.4.3 puts the trail above
        // the data, so the trail is what the transaction protects.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Logged before the select, and logged even when no detail comes back: an attempt to
        // resolve an address is the thing worth seeing in the trail, whether or not one exists.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO sensitive.ReceiverDetailAccessLog (ReceiverRef, PrincipalId, Reason)
            VALUES (@receiverRef, @principalId, @reason)
            """,
            new { receiverRef, principalId, reason },
            transaction,
            cancellationToken: cancellationToken));

        var detail = await connection.QuerySingleOrDefaultAsync<ReceiverDetailReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM sensitive.ReceiverDetail WHERE ReceiverRef = @receiverRef",
            new { receiverRef },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

        return detail;
    }

    public async Task UpsertAsync(ReceiverDetailReadModel detail, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // MERGE is avoided deliberately; UPDATE-then-INSERT is easier to read and this table
        // sees a handful of writes a month.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE sensitive.ReceiverDetail SET
                ContactName = @ContactName,
                ContactPhone = @ContactPhone,
                AddressLine1 = @AddressLine1,
                AddressLine2 = @AddressLine2,
                City = @City,
                PostCode = @PostCode,
                DeleteAfter = @DeleteAfter
            WHERE ReceiverRef = @Ref
            """,
            detail,
            cancellationToken: cancellationToken));

        if (affected == 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO sensitive.ReceiverDetail
                    (ReceiverRef, ContactName, ContactPhone, AddressLine1, AddressLine2, City, PostCode, DeleteAfter)
                VALUES
                    (@Ref, @ContactName, @ContactPhone, @AddressLine1, @AddressLine2, @City, @PostCode, @DeleteAfter)
                """,
                detail,
                cancellationToken: cancellationToken));
        }
    }

    public async Task<bool> DeleteAsync(Guid receiverRef, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // The access log is not deleted with the detail. The record of who read an address
        // outlives the address itself — that is the point of keeping it.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM sensitive.ReceiverDetail WHERE ReceiverRef = @receiverRef",
            new { receiverRef },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<int> CountAccessesAsync(Guid receiverRef, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM sensitive.ReceiverDetailAccessLog WHERE ReceiverRef = @receiverRef",
            new { receiverRef },
            cancellationToken: cancellationToken));
    }
}
