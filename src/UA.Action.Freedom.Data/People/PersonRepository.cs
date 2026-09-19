using Dapper;
using Microsoft.Data.SqlClient;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Data.People;

/// <summary>
/// Dapper-backed <see cref="IPersonRepository"/> over the split volunteer identity:
/// <c>dbo.Person</c> (the anonymous key everything else points at) and <c>dbo.PersonDetail</c>
/// (the personal data). Every statement is parameterised.
/// </summary>
/// <remarks>
/// Reads come from <c>dbo.PersonDetail</c> alone, so an erased volunteer — whose detail row is
/// gone — is simply not found, anywhere, without any caller having to remember to filter.
/// Volunteer personal data, so nothing here logs a row or a parameter (recommendations §4.8).
/// </remarks>
public sealed class PersonRepository(IDbConnectionFactory connectionFactory) : IPersonRepository
{
    private const string Columns =
        "PersonId AS Id, FirstName, LastName, DateOfBirth, Joined, Phone, IsDriver, Committed";

    /// <summary>The manifest states that say what became of the load: its team is history.</summary>
    private static readonly string FinishedStatuses =
        $"({(int)ManifestStatus.Delivered}, {(int)ManifestStatus.Lost}, {(int)ManifestStatus.Returned})";

    public async Task<PersonReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<PersonReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM dbo.PersonDetail WHERE PersonId = @id",
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
             SELECT {Columns} FROM dbo.PersonDetail
             WHERE (@driversOnly = 0 OR IsDriver = 1)
             ORDER BY LastName, FirstName, PersonId
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
            "SELECT COUNT(1) FROM dbo.PersonDetail WHERE PersonId = @id",
            new { id },
            cancellationToken: cancellationToken));

        return count > 0;
    }

    public async Task AddAsync(PersonReadModel person, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        // Identity and personal data arrive together or not at all.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.Person (Id) VALUES (@Id);
            INSERT INTO dbo.PersonDetail
                (PersonId, FirstName, LastName, DateOfBirth, Joined, Phone, IsDriver, Committed)
            VALUES
                (@Id, @FirstName, @LastName, @DateOfBirth, @Joined, @Phone, @IsDriver, @Committed);
            """,
            person,
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(PersonReadModel person, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.PersonDetail SET
                FirstName = @FirstName,
                LastName = @LastName,
                DateOfBirth = @DateOfBirth,
                Joined = @Joined,
                Phone = @Phone,
                IsDriver = @IsDriver,
                Committed = @Committed,
                UpdatedAt = SYSUTCDATETIME()
            WHERE PersonId = @Id
            """,
            person,
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<DeletePersonResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Refused while the person is still needed: on the crew of a convoy that has not arrived,
        // or on the team of a manifest whose load is not yet delivered, lost or returned. The
        // UPDLOCK on their detail row stops an erasure racing another erasure of the same person.
        var status = await connection.QuerySingleAsync<(bool Found, bool Active)>(new CommandDefinition(
            $"""
             SELECT
                 CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.PersonDetail WITH (UPDLOCK) WHERE PersonId = @id)
                      THEN 1 ELSE 0 END AS bit) AS Found,
                 CAST(CASE WHEN EXISTS (
                          SELECT 1 FROM dbo.VehicleDriver AS vd
                          JOIN dbo.Convoy AS c ON c.Id = vd.ConvoyId
                          WHERE vd.PersonId = @id AND c.ArrivedAt IS NULL)
                      OR EXISTS (
                          SELECT 1 FROM dbo.ManifestDriverTeam AS t
                          JOIN dbo.Manifest AS m ON m.Id = t.ManifestId
                          WHERE (t.PrimaryPersonId = @id OR t.SecondaryPersonId = @id)
                            AND m.Status NOT IN {FinishedStatuses})
                      THEN 1 ELSE 0 END AS bit) AS Active
             """,
            new { id },
            transaction,
            cancellationToken: cancellationToken));

        if (!status.Found)
        {
            return DeletePersonResult.NotFound;
        }

        if (status.Active)
        {
            return DeletePersonResult.StillActive;
        }

        // The erasure proper: the personal data goes.
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.PersonDetail WHERE PersonId = @id",
            new { id },
            transaction,
            cancellationToken: cancellationToken));

        // Then the identity too, if nothing names it. Past crews, teams and box records do, and
        // for them it stays — an anonymous key they can still point at — stamped as erased.
        // Asking the database, rather than listing those tables here, keeps a sixth reference
        // from turning into a 500.
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.Person WHERE Id = @id",
                new { id },
                transaction,
                cancellationToken: cancellationToken));
        }
        catch (SqlException exception) when (exception.Number == SqlErrors.ForeignKeyViolation)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.Person SET ErasedAt = SYSUTCDATETIME() WHERE Id = @id",
                new { id },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return DeletePersonResult.Deleted;
    }
}
