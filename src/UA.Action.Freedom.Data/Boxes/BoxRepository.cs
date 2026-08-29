using System.Text.Json;
using Dapper;
using UA.Action.Freedom.Application.Boxes;

namespace UA.Action.Freedom.Data.Boxes;

/// <summary>
/// Dapper-backed <see cref="IBoxRepository"/> over <c>dbo.Box</c> and <c>dbo.BoxItem</c>.
/// </summary>
public sealed class BoxRepository(IDbConnectionFactory connectionFactory) : IBoxRepository
{
    private const string Columns =
        "Id, WeightKg, ReceiverRef, House, Street, City, Country, Postcode, ValidatedByPersonId, ValidatedAt";

    /// <summary>
    /// Item properties are an open-ended bag stored as JSON, so they cannot be hydrated by
    /// Dapper's constructor mapping the way every other read model is. This row type is the
    /// seam: Dapper fills it from the columns, and <see cref="ToItem"/> turns it into the shape
    /// the application works with.
    /// </summary>
    private sealed record BoxItemRow(Guid Id, string Description, string PropertiesJson);

    private static BoxItemReadModel ToItem(BoxItemRow row) => new(
        row.Id,
        row.Description,
        JsonSerializer.Deserialize<Dictionary<string, string>>(row.PropertiesJson) ?? []);

    public async Task<BoxReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<BoxReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM dbo.Box WHERE Id = @id",
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<BoxReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<BoxReadModel>(new CommandDefinition(
            $"""
             SELECT {Columns} FROM dbo.Box
             ORDER BY Id
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
            INSERT INTO dbo.Box (ReceiverRef, House, Street, City, Country, Postcode)
            VALUES (@ReceiverRef, @House, @Street, @City, @Country, @Postcode);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """,
            box,
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
                House = @House,
                Street = @Street,
                City = @City,
                Country = @Country,
                Postcode = @Postcode,
                UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id
            """,
            box,
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
        int id, Guid validatedByPersonId, int weightKg, DateTime validatedAt, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Conditional on the box not already being validated, so the database settles a race
        // between two Loaders checking the same box rather than the application reading first.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Box SET
                WeightKg = @weightKg,
                ValidatedByPersonId = @validatedByPersonId,
                ValidatedAt = @validatedAt,
                UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @id AND ValidatedAt IS NULL
            """,
            new { id, validatedByPersonId, weightKg, validatedAt },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<IReadOnlyList<BoxItemReadModel>> ListItemsAsync(int boxId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<BoxItemRow>(new CommandDefinition(
            "SELECT Id, Description, PropertiesJson FROM dbo.BoxItem WHERE BoxId = @boxId ORDER BY Description, Id",
            new { boxId },
            cancellationToken: cancellationToken));

        return rows.Select(ToItem).ToList();
    }

    public async Task AddItemAsync(int boxId, BoxItemReadModel item, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.BoxItem (Id, BoxId, Description, PropertiesJson)
            VALUES (@id, @boxId, @description, @propertiesJson)
            """,
            new
            {
                id = item.Id,
                boxId,
                description = item.Description,
                propertiesJson = JsonSerializer.Serialize(item.Properties),
            },
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
}
