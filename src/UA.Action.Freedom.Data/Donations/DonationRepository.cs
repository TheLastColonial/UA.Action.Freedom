using Dapper;
using Microsoft.Data.SqlClient;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Donations;

namespace UA.Action.Freedom.Data.Donations;

/// <summary>
/// Dapper-backed <see cref="IDonationRepository"/> over <c>dbo.Donation</c>. The donor name is read through
/// <c>dbo.DonorDisplay</c>, the one place "Former donor" is decided.
/// </summary>
public sealed class DonationRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution) : IDonationRepository
{
    private static readonly string Columns =
        $"n.Id, n.DonorId, donor.DisplayName AS DonorName, n.ReceivedOn, n.Notes, {ChangeStamp.ReadColumns("n")}";

    private static readonly string From =
        $"dbo.Donation AS n JOIN dbo.DonorDisplay AS donor ON donor.DonorId = n.DonorId {ChangeStamp.ReadJoin("n")}";

    /// <summary>
    /// Dapper maps a <c>date</c> column to <see cref="DateTime"/> and cannot fill a <see cref="DateOnly"/> through a
    /// constructor, so rows are read into this seam and turned into the read model here.
    /// </summary>
    private sealed record DonationRow(
        int Id, Guid DonorId, string DonorName, DateTime ReceivedOn, string? Notes,
        string? LastChangedByName, DateTime? LastChangedAt);

    private static DonationReadModel ToDonation(DonationRow row) => new(
        row.Id, row.DonorId, row.DonorName, DateOnly.FromDateTime(row.ReceivedOn), row.Notes,
        row.LastChangedByName, row.LastChangedAt);

    public async Task<DonationReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var row = await connection.QuerySingleOrDefaultAsync<DonationRow>(new CommandDefinition(
            $"SELECT {Columns} FROM {From} WHERE n.Id = @id",
            new { id },
            cancellationToken: cancellationToken));

        return row is null ? null : ToDonation(row);
    }

    public async Task<IReadOnlyList<DonationReadModel>> ListAsync(
        Guid? donorId, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<DonationRow>(new CommandDefinition(
            $"""
             SELECT {Columns} FROM {From}
             WHERE (@donorId IS NULL OR n.DonorId = @donorId)
             ORDER BY n.ReceivedOn DESC, n.Id DESC
             OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
             """,
            new { donorId, skip = (page - 1) * pageSize, take = pageSize },
            cancellationToken: cancellationToken));

        return rows.Select(ToDonation).ToList();
    }

    public async Task<int> AddAsync(Guid donorId, DateOnly receivedOn, string? notes, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO dbo.Donation (DonorId, ReceivedOn, Notes, LastChangedBy, LastChangedAt)
            VALUES (@donorId, @receivedOn, @notes, @changedBy, SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """,
            attribution.With(new { donorId, receivedOn, notes }),
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(int id, DateOnly receivedOn, string? notes, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Donation SET
                ReceivedOn = @receivedOn,
                Notes = @notes,
                LastChangedBy = @changedBy,
                LastChangedAt = SYSUTCDATETIME()
            WHERE Id = @id
            """,
            attribution.With(new { id, receivedOn, notes }),
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<DeleteDonationResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        try
        {
            var affected = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.Donation WHERE Id = @id",
                new { id },
                cancellationToken: cancellationToken));

            return affected > 0 ? DeleteDonationResult.Deleted : DeleteDonationResult.NotFound;
        }
        catch (SqlException exception) when (exception.Number == SqlErrors.ForeignKeyViolation)
        {
            return DeleteDonationResult.StillReferenced;
        }
    }

    public async Task<string?> DonorNameAsync(Guid donorId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT DisplayName FROM dbo.DonorDisplay WHERE DonorId = @donorId",
            new { donorId },
            cancellationToken: cancellationToken));
    }

    private sealed record ReportRow(
        int DonationId, DateTime ReceivedOn, string CategoryNameEn, int Quantity, decimal? ValueGbp, bool BoxValidated);

    public async Task<IReadOnlyList<DonorReportItem>> ReportItemsAsync(Guid donorId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Only what a donor may be told. The box is read for one fact, whether it has been validated: no
        // receiver, location, route or manifest is selected, so none can reach the report.
        var rows = await connection.QueryAsync<ReportRow>(new CommandDefinition(
            """
            SELECT
                n.Id AS DonationId,
                n.ReceivedOn,
                c.NameEn AS CategoryNameEn,
                COALESCE(i.Quantity, 1) AS Quantity,
                i.ValueGbp,
                CAST(CASE WHEN b.ValidatedAt IS NULL THEN 0 ELSE 1 END AS bit) AS BoxValidated
            FROM dbo.Donation AS n
            JOIN dbo.BoxItem AS i ON i.DonationId = n.Id
            JOIN dbo.ItemCategory AS c ON c.Id = i.CategoryId
            JOIN dbo.Box AS b ON b.Id = i.BoxId
            WHERE n.DonorId = @donorId AND b.VoidedAt IS NULL
            ORDER BY n.ReceivedOn, n.Id, c.NameEn, i.Id
            """,
            new { donorId },
            cancellationToken: cancellationToken));

        return rows
            .Select(row => new DonorReportItem(
                row.DonationId, DateOnly.FromDateTime(row.ReceivedOn), row.CategoryNameEn, row.Quantity, row.ValueGbp, row.BoxValidated))
            .ToList();
    }
}
