using Dapper;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Data.People;

/// <summary>
/// Dapper-backed <see cref="IPersonRepository"/> over <c>dbo.Person</c>. Every statement is
/// parameterised; the write methods return the affected-row count as a bool so the handlers
/// can tell "no such person" from "done".
/// </summary>
/// <remarks>
/// Volunteer personal data, so nothing here logs a row or a parameter (recommendations §4.8).
/// </remarks>
public sealed class PersonRepository(IDbConnectionFactory connectionFactory) : IPersonRepository
{
    private const string Columns =
        "Id, FirstName, LastName, DateOfBirth, Joined, Phone, IsDriver, Committed";

    public async Task<PersonReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<PersonReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM dbo.Person WHERE Id = @id",
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<PersonReadModel>> ListAsync(
        int page, int pageSize, bool driversOnly, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // The filter is a parameter rather than two statements: @driversOnly = 0 leaves every
        // row eligible, so one query plan serves both the full roster and the driver shortlist.
        var rows = await connection.QueryAsync<PersonReadModel>(new CommandDefinition(
            $"""
             SELECT {Columns} FROM dbo.Person
             WHERE (@driversOnly = 0 OR IsDriver = 1)
             ORDER BY LastName, FirstName, Id
             OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
             """,
            new { driversOnly, skip = (page - 1) * pageSize, take = pageSize },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.Person WHERE Id = @id",
            new { id },
            cancellationToken: cancellationToken));

        return count > 0;
    }

    public async Task AddAsync(PersonReadModel person, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.Person
                (Id, FirstName, LastName, DateOfBirth, Joined, Phone, IsDriver, Committed)
            VALUES
                (@Id, @FirstName, @LastName, @DateOfBirth, @Joined, @Phone, @IsDriver, @Committed)
            """,
            person,
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(PersonReadModel person, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Person SET
                FirstName = @FirstName,
                LastName = @LastName,
                DateOfBirth = @DateOfBirth,
                Joined = @Joined,
                Phone = @Phone,
                IsDriver = @IsDriver,
                Committed = @Committed,
                UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id
            """,
            person,
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.Person WHERE Id = @id",
            new { id },
            cancellationToken: cancellationToken));

        return affected > 0;
    }
}
