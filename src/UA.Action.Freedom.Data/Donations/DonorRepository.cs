using Dapper;
using Microsoft.Data.SqlClient;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Donations;

namespace UA.Action.Freedom.Data.Donations;

/// <summary>
/// Dapper-backed <see cref="IDonorRepository"/> over the split donor identity: <c>dbo.Donor</c> (the anonymous
/// key a donation points at) and <c>dbo.DonorDetail</c> (the personal data). The pattern of
/// <c>PersonRepository</c>, minus any refusal: a donor has no operational dependency, so erasure always proceeds.
/// </summary>
/// <remarks>
/// Reads come from <c>dbo.DonorDetail</c> alone, so an erased donor is simply not found. Personal data: nothing
/// here logs a row or a parameter.
/// </remarks>
public sealed class DonorRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution) : IDonorRepository
{
    private static readonly string Columns =
        $"d.DonorId AS Id, d.Name, d.Email, d.Phone, {ChangeStamp.ReadColumns("d")}";

    private static readonly string From = $"dbo.DonorDetail AS d {ChangeStamp.ReadJoin("d")}";

    public async Task<DonorReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<DonorReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM {From} WHERE d.DonorId = @id",
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<DonorReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<DonorReadModel>(new CommandDefinition(
            $"""
             SELECT {Columns} FROM {From}
             ORDER BY d.Name, d.DonorId
             OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
             """,
            new { skip = (page - 1) * pageSize, take = pageSize },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task AddAsync(DonorReadModel donor, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        // Identity and personal data arrive together or not at all.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.Donor (Id) VALUES (@Id);
            INSERT INTO dbo.DonorDetail (DonorId, Name, Email, Phone, LastChangedBy, LastChangedAt)
            VALUES (@Id, @Name, @Email, @Phone, @changedBy, SYSUTCDATETIME());
            """,
            attribution.With(new { donor.Id, donor.Name, donor.Email, donor.Phone }),
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(DonorReadModel donor, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.DonorDetail SET
                Name = @Name,
                Email = @Email,
                Phone = @Phone,
                LastChangedBy = @changedBy,
                LastChangedAt = SYSUTCDATETIME()
            WHERE DonorId = @Id
            """,
            attribution.With(new { donor.Id, donor.Name, donor.Email, donor.Phone }),
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> EraseAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The erasure proper: the personal data goes. The affected count says whether there was a donor at all.
        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.DonorDetail WHERE DonorId = @id",
            new { id },
            transaction,
            cancellationToken: cancellationToken));

        if (deleted == 0)
        {
            return false;
        }

        // Then the identity too, if no donation names it; otherwise it stays as the anonymous key, stamped erased.
        // Asking the database, rather than listing the referencing tables here, keeps a later reference from
        // turning into a 500.
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.Donor WHERE Id = @id",
                new { id },
                transaction,
                cancellationToken: cancellationToken));
        }
        catch (SqlException exception) when (exception.Number == SqlErrors.ForeignKeyViolation)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.Donor SET ErasedAt = SYSUTCDATETIME() WHERE Id = @id",
                new { id },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
