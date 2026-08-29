using Dapper;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Data.Convoys;

/// <summary>
/// Dapper-backed <see cref="IConvoyRepository"/> over <c>dbo.Convoy</c>,
/// <c>dbo.ConvoyRouteStop</c> and the <c>ConvoyId</c> column of <c>dbo.Vehicle</c>. Every
/// statement is parameterised; the write methods return the affected-row count as a bool so
/// the handlers can tell "no such row" from "done".
/// </summary>
public sealed class ConvoyRepository(IDbConnectionFactory connectionFactory) : IConvoyRepository
{
    private const string Columns = "Id, Start, ExpectedEnd, TruckListPublishedAt";

    private const string StopColumns = "Sequence, House, Street, City, Country, Postcode";

    public async Task<ConvoyReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<ConvoyReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM dbo.Convoy WHERE Id = @id",
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ConvoyReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Newest departure first: the convoy people are working on is almost always the next
        // one, and convoys run about once a month so the list is short and mostly historical.
        var rows = await connection.QueryAsync<ConvoyReadModel>(new CommandDefinition(
            $"""
             SELECT {Columns} FROM dbo.Convoy
             ORDER BY Start DESC, Id DESC
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
            "SELECT COUNT(1) FROM dbo.Convoy WHERE Id = @id",
            new { id },
            cancellationToken: cancellationToken));

        return count > 0;
    }

    public async Task<int> AddAsync(DateTime start, DateTime expectedEnd, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // SCOPE_IDENTITY rather than @@IDENTITY: the latter would return an identity created by
        // a trigger on this table instead of the row just inserted.
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO dbo.Convoy (Start, ExpectedEnd) VALUES (@start, @expectedEnd);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """,
            new { start, expectedEnd },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(ConvoyReadModel convoy, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // TruckListPublishedAt is deliberately absent: publishing is its own transition, and an
        // ordinary update must not be able to stamp or clear it.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Convoy SET
                Start = @Start,
                ExpectedEnd = @ExpectedEnd,
                UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id
            """,
            convoy,
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Route stops cascade; vehicles are released to ConvoyId NULL by the foreign key.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.Convoy WHERE Id = @id",
            new { id },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<IReadOnlyList<RouteStopReadModel>> GetRouteAsync(int convoyId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<RouteStopReadModel>(new CommandDefinition(
            $"SELECT {StopColumns} FROM dbo.ConvoyRouteStop WHERE ConvoyId = @convoyId ORDER BY Sequence",
            new { convoyId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task ReplaceRouteAsync(
        int convoyId, IReadOnlyList<RouteStopReadModel> stops, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        // The only transaction in the codebase, and it earns it: a route is meaningful only as
        // a whole journey. Deleting the old stops and failing part-way through inserting the new
        // ones would leave the convoy with a truncated route that still looks valid.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.ConvoyRouteStop WHERE ConvoyId = @convoyId",
            new { convoyId },
            transaction,
            cancellationToken: cancellationToken));

        if (stops.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                $"""
                 INSERT INTO dbo.ConvoyRouteStop (ConvoyId, {StopColumns})
                 VALUES (@ConvoyId, @Sequence, @House, @Street, @City, @Country, @Postcode)
                 """,
                stops.Select(stop => new
                {
                    ConvoyId = convoyId,
                    stop.Sequence,
                    stop.House,
                    stop.Street,
                    stop.City,
                    stop.Country,
                    stop.Postcode,
                }).ToList(),
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConvoyVehicleReadModel>> ListVehiclesAsync(
        int convoyId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<ConvoyVehicleReadModel>(new CommandDefinition(
            "SELECT Vin, Plate, WeightKg FROM dbo.Vehicle WHERE ConvoyId = @convoyId ORDER BY Vin",
            new { convoyId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<bool> AssignVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.Vehicle SET ConvoyId = @convoyId, UpdatedAt = SYSUTCDATETIME() WHERE Vin = @vin",
            new { convoyId, vin },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> UnassignVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Scoped to this convoy: removing a vehicle from a convoy it was never on is a caller
        // mistake worth reporting, not a silent success that clears someone else's truck list.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Vehicle SET ConvoyId = NULL, UpdatedAt = SYSUTCDATETIME()
            WHERE Vin = @vin AND ConvoyId = @convoyId
            """,
            new { convoyId, vin },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> PublishTruckListAsync(int convoyId, DateTime publishedAt, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Conditional on nothing having published yet, so the database settles a race between
        // two dispatchers rather than the application reading and then writing.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Convoy SET
                TruckListPublishedAt = @publishedAt,
                UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @convoyId AND TruckListPublishedAt IS NULL
            """,
            new { convoyId, publishedAt },
            cancellationToken: cancellationToken));

        return affected > 0;
    }
}
