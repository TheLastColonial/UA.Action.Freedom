using Dapper;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Locations;

namespace UA.Action.Freedom.Data.Locations;

/// <summary>
/// Dapper-backed <see cref="ILocationRepository"/> over <c>dbo.Location</c>.
/// </summary>
public sealed class LocationRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution)
    : ILocationRepository
{
    private static readonly string Columns =
        $"l.Id, l.Name, l.House, l.Street, l.City, l.Country, l.Postcode, l.IsRegisteredHub, {ChangeStamp.ReadColumns("l")}";

    private static readonly string From = $"dbo.Location AS l {ChangeStamp.ReadJoin("l")}";

    public async Task<LocationReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<LocationReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM {From} WHERE l.Id = @id",
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<LocationReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<LocationReadModel>(new CommandDefinition(
            $"""
             SELECT {Columns} FROM {From}
             ORDER BY l.Id
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
            "SELECT COUNT(1) FROM dbo.Location WHERE Id = @id",
            new { id },
            cancellationToken: cancellationToken));

        return count > 0;
    }

    public async Task<int> AddAsync(LocationReadModel location, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO dbo.Location (Name, House, Street, City, Country, Postcode, IsRegisteredHub, LastChangedBy, LastChangedAt)
            VALUES (@Name, @House, @Street, @City, @Country, @Postcode, @IsRegisteredHub, @changedBy, SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """,
            attribution.With(location),
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(LocationReadModel location, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Location SET
                Name = @Name,
                House = @House,
                Street = @Street,
                City = @City,
                Country = @Country,
                Postcode = @Postcode,
                IsRegisteredHub = @IsRegisteredHub,
                UpdatedAt = SYSUTCDATETIME(),
                LastChangedBy = @changedBy,
                LastChangedAt = SYSUTCDATETIME()
            WHERE Id = @Id
            """,
            attribution.With(location),
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.Location WHERE Id = @id",
            new { id },
            cancellationToken: cancellationToken));

        return affected > 0;
    }
}
