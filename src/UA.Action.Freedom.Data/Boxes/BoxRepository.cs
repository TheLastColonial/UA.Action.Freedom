using System.Text.Json;
using Dapper;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Boxes;

namespace UA.Action.Freedom.Data.Boxes;

/// <summary>
/// Dapper-backed <see cref="IBoxRepository"/> over <c>dbo.Box</c>, <c>dbo.BoxItem</c> and
/// <c>dbo.BoxQrCode</c>.
/// </summary>
public sealed class BoxRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution) : IBoxRepository
{
    private static readonly string Columns =
        $"b.Id, b.WeightKg, b.WidthCm, b.DepthCm, b.HeightCm, b.ReceiverRef, b.LocationId, b.ValidatedByPersonId, b.ValidatedAt, {ChangeStamp.ReadColumns("b")}";

    private static readonly string From = $"dbo.Box AS b {ChangeStamp.ReadJoin("b")}";

    private const string QrCodeColumns = "Token, BoxId, IssuedAt, RevokedAt";

    private const string BayAssignmentColumns = "Id, BoxId, BayId, AssignedByPersonId, AssignedAt, VacatedAt";

    /// <summary>
    /// Item properties are an open-ended bag stored as JSON, so they cannot be hydrated by
    /// Dapper's constructor mapping the way every other read model is. This row type is the
    /// seam: Dapper fills it from the columns, and <see cref="ToItem"/> turns it into the shape
    /// the application works with.
    /// </summary>
    private sealed record BoxItemRow(
        Guid Id, string Description, string PropertiesJson, string? CommodityCode);

    private static BoxItemReadModel ToItem(BoxItemRow row) => new(
        row.Id,
        row.Description,
        ReadProperties(row.PropertiesJson),
        row.CommodityCode);

    private static Dictionary<string, string> ReadProperties(string json) =>
        (JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [])
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value.ValueKind == JsonValueKind.String
                    ? entry.Value.GetString() ?? string.Empty
                    : entry.Value.GetRawText());

    public async Task<BoxReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<BoxReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM {From} WHERE b.Id = @id",
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<BoxReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<BoxReadModel>(new CommandDefinition(
            $"""
             SELECT {Columns} FROM {From}
             ORDER BY b.Id
             OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
             """,
            new { skip = (page - 1) * pageSize, take = pageSize },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.Box WHERE Id = @id",
            new { id },
            cancellationToken: cancellationToken));

        return count > 0;
    }

    public async Task<int> AddAsync(BoxReadModel box, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Neither weight nor validation is insertable: a box is born unvalidated and weighing
        // nothing, and the only way past that is ValidateAsync.
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO dbo.Box (ReceiverRef, LocationId, LastChangedBy, LastChangedAt)
            VALUES (@ReceiverRef, @LocationId, @changedBy, SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """,
            attribution.With(box),
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(BoxReadModel box, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // WeightKg, ValidatedByPersonId and ValidatedAt are absent on purpose. There is no way
        // to forge a validation, or to alter a confirmed weight, by sending an ordinary update.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Box SET
                ReceiverRef = @ReceiverRef,
                LocationId = @LocationId,
                UpdatedAt = SYSUTCDATETIME(),
                LastChangedBy = @changedBy,
                LastChangedAt = SYSUTCDATETIME()
            WHERE Id = @Id
            """,
            attribution.With(box),
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Items cascade: they have no life outside the box.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.Box WHERE Id = @id",
            new { id },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> ValidateAsync(
        int id, Guid validatedByPersonId, int weightKg,
        decimal? widthCm, decimal? depthCm, decimal? heightCm,
        DateTime validatedAt, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Conditional on the box not already being validated, so the database settles a race
        // between two Loaders checking the same box rather than the application reading first.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Box SET
                WeightKg = @weightKg,
                WidthCm = @widthCm,
                DepthCm = @depthCm,
                HeightCm = @heightCm,
                ValidatedByPersonId = @validatedByPersonId,
                ValidatedAt = @validatedAt,
                UpdatedAt = SYSUTCDATETIME(),
                LastChangedBy = @changedBy,
                LastChangedAt = SYSUTCDATETIME()
            WHERE Id = @id AND ValidatedAt IS NULL
            """,
            attribution.With(new { id, validatedByPersonId, weightKg, widthCm, depthCm, heightCm, validatedAt }),
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<IReadOnlyList<BoxItemReadModel>> ListItemsAsync(int boxId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<BoxItemRow>(new CommandDefinition(
            "SELECT Id, Description, PropertiesJson, CommodityCode FROM dbo.BoxItem "
            + "WHERE BoxId = @boxId ORDER BY Description, Id",
            new { boxId },
            cancellationToken: cancellationToken));

        return rows.Select(ToItem).ToList();
    }

    public async Task AddItemAsync(int boxId, BoxItemReadModel item, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.BoxItem (Id, BoxId, Description, PropertiesJson, CommodityCode, LastChangedBy, LastChangedAt)
            VALUES (@id, @boxId, @description, @propertiesJson, @commodityCode, @changedBy, SYSUTCDATETIME())
            """,
            attribution.With(new
            {
                id = item.Id,
                boxId,
                description = item.Description,
                propertiesJson = JsonSerializer.Serialize(item.Properties),
                commodityCode = item.CommodityCode,
            }),
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteItemAsync(int boxId, Guid itemId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Scoped to the box: unpacking an item from a box it was never in is a caller mistake
        // worth reporting, not a silent success that empties somebody else's box.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.BoxItem WHERE Id = @itemId AND BoxId = @boxId",
            new { boxId, itemId },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<BoxQrCodeReadModel?> GetActiveQrCodeAsync(int boxId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<BoxQrCodeReadModel>(new CommandDefinition(
            $"SELECT {QrCodeColumns} FROM dbo.BoxQrCode WHERE BoxId = @boxId AND RevokedAt IS NULL",
            new { boxId },
            cancellationToken: cancellationToken));
    }

    public async Task<BoxQrCodeReadModel?> ResolveActiveQrCodeAsync(Guid token, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Active only: a revoked token names a label that has been replaced, and it must read
        // as unknown rather than resolve to a box it no longer identifies.
        return await connection.QuerySingleOrDefaultAsync<BoxQrCodeReadModel>(new CommandDefinition(
            $"SELECT {QrCodeColumns} FROM dbo.BoxQrCode WHERE Token = @token AND RevokedAt IS NULL",
            new { token },
            cancellationToken: cancellationToken));
    }

    public async Task<BoxQrCodeReadModel> IssueQrCodeAsync(
        int boxId, Guid token, DateTime issuedAt, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        // The second transaction in the codebase, and it earns it the same way ReplaceRouteAsync
        // does: re-labelling is one act. Revoking the old code and failing before the new one is
        // inserted would leave the box with no label a scan resolves to. The revoke is
        // conditional on RevokedAt IS NULL so the database settles a concurrent double-issue.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.BoxQrCode SET RevokedAt = @issuedAt, LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
            WHERE BoxId = @boxId AND RevokedAt IS NULL
            """,
            attribution.With(new { boxId, issuedAt }),
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.BoxQrCode (Token, BoxId, IssuedAt, LastChangedBy, LastChangedAt)
            VALUES (@token, @boxId, @issuedAt, @changedBy, SYSUTCDATETIME())
            """,
            attribution.With(new { token, boxId, issuedAt }),
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

        return new BoxQrCodeReadModel(token, boxId, issuedAt, RevokedAt: null);
    }

    public async Task<bool> RevokeActiveQrCodeAsync(int boxId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.BoxQrCode SET RevokedAt = SYSUTCDATETIME(), LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
            WHERE BoxId = @boxId AND RevokedAt IS NULL
            """,
            attribution.With(new { boxId }),
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<BoxBayAssignmentReadModel?> GetActiveBayAssignmentAsync(int boxId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<BoxBayAssignmentReadModel>(new CommandDefinition(
            $"SELECT {BayAssignmentColumns} FROM dbo.BoxBayAssignment WHERE BoxId = @boxId AND VacatedAt IS NULL",
            new { boxId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<BoxBayAssignmentReadModel>> ListBayAssignmentHistoryAsync(int boxId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<BoxBayAssignmentReadModel>(new CommandDefinition(
            $"SELECT {BayAssignmentColumns} FROM dbo.BoxBayAssignment WHERE BoxId = @boxId ORDER BY AssignedAt DESC, Id DESC",
            new { boxId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<BoxBayAssignmentReadModel> AssignBayAsync(
        int boxId, int bayId, Guid assignedByPersonId, DateTime assignedAt, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        // Mirrors IssueQrCodeAsync: vacating the old assignment and inserting the new one is one
        // act, so the box is never briefly in two bays at once. The vacate is conditional on
        // VacatedAt IS NULL so the database settles a concurrent double-assign.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.BoxBayAssignment SET VacatedAt = @assignedAt WHERE BoxId = @boxId AND VacatedAt IS NULL",
            new { boxId, assignedAt },
            transaction,
            cancellationToken: cancellationToken));

        var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO dbo.BoxBayAssignment (BoxId, BayId, AssignedByPersonId, AssignedAt)
            VALUES (@boxId, @bayId, @assignedByPersonId, @assignedAt);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """,
            new { boxId, bayId, assignedByPersonId, assignedAt },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

        return new BoxBayAssignmentReadModel(id, boxId, bayId, assignedByPersonId, assignedAt, VacatedAt: null);
    }

    public async Task<bool> VacateActiveBayAssignmentAsync(int boxId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.BoxBayAssignment SET VacatedAt = SYSUTCDATETIME() WHERE BoxId = @boxId AND VacatedAt IS NULL",
            new { boxId },
            cancellationToken: cancellationToken));

        return affected > 0;
    }
}
