using Dapper;
using Microsoft.Data.SqlClient;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Data.Convoys;

/// <summary>
/// Dapper-backed <see cref="IConvoyRepository"/> over <c>dbo.Convoy</c> and
/// <c>dbo.ConvoyRouteStop</c> — the convoy as a journey. The truck list, the crew and the
/// insurance live in <see cref="ConvoyVehicleRepository"/>.
/// </summary>
/// <remarks>
/// Every statement is parameterised; the write methods return the affected-row count as a bool so
/// the handlers can tell "no such row" from "done".
/// </remarks>
public sealed class ConvoyRepository(IDbConnectionFactory connectionFactory) : IConvoyRepository
{
    private const string Columns = "Id, Start, ExpectedEnd, TruckListPublishedAt, ArrivedAt";

    /// <summary>A manifest in one of these says what became of its vehicle; the journey is over for it.</summary>
    private static readonly string FinishedStatuses =
        $"({(int)ManifestStatus.Delivered}, {(int)ManifestStatus.Lost}, {(int)ManifestStatus.Returned})";

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

        // TruckListPublishedAt and ArrivedAt are deliberately absent: each is its own transition,
        // and an ordinary update must not be able to stamp or clear either.
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

    public async Task<DeleteResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        // Route stops cascade. The truck list does not: FK_ConvoyVehicle_Convoy is NO ACTION so
        // that the crew and insurance below it may cascade from Vehicle instead, SQL Server
        // allowing only one cascade path into each. Deleting the truck-list rows here takes the
        // crew and insurance with them, in the same transaction as the convoy delete.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // FK_Manifest_ConvoyVehicle is NO ACTION: a convoy with manifests travelled, or is about
        // to, and those manifests are the record of it. Both statements are inside the try,
        // because the refusal now comes from the *first* of them — the truck-list delete is what
        // a manifest blocks, and the convoy delete never gets that far. Disposing the transaction
        // without committing rolls the whole thing back.
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.ConvoyVehicle WHERE ConvoyId = @id",
                new { id },
                transaction,
                cancellationToken: cancellationToken));

            var affected = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.Convoy WHERE Id = @id",
                new { id },
                transaction,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            return affected > 0 ? DeleteResult.Deleted : DeleteResult.NotFound;
        }
        catch (SqlException exception) when (exception.Number == SqlErrors.ForeignKeyViolation)
        {
            return DeleteResult.StillReferenced;
        }
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

        // A route is meaningful only as a whole journey. Deleting the old stops and failing part-way through inserting the new
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

    public async Task<ArriveResult> ArriveAsync(int convoyId, DateTime arrivedAt, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        // Arrival and handover are one fact about the journey: a convoy marked arrived whose
        // Delivered vehicles are still offered for the next one, or the reverse, must not be
        // possible.
        //
        // The convoy row is taken first, by the conditional UPDATE that stamps it: a second
        // dispatcher's arrival then waits on that one row rather than both succeeding. The
        // still-travelling check is an ordinary read after it — the truck list is published, so
        // no vehicle can join meanwhile — and nothing here scans dbo.Vehicle under a lock, which
        // is what deadlocked this transaction against single-vehicle writes.
        //
        // Nothing is released any more. A vehicle that was not handed over is free for the next
        // convoy because this one has arrived, which ConvoyVehicleRepository.AddAsync asks
        // directly — there is no pointer left to clear.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var arrived = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Convoy SET ArrivedAt = @arrivedAt, UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @convoyId AND ArrivedAt IS NULL AND TruckListPublishedAt IS NOT NULL
            """,
            new { convoyId, arrivedAt },
            transaction,
            cancellationToken: cancellationToken));

        if (arrived == 0)
        {
            return ArriveResult.AlreadyArrived;
        }

        var stillTravelling = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"""
             SELECT COUNT(1)
             FROM dbo.ConvoyVehicle AS cv
             WHERE cv.ConvoyId = @convoyId
               AND cv.WithdrawnAt IS NULL
               AND NOT EXISTS (SELECT 1 FROM dbo.Manifest AS m
                               WHERE m.ConvoyId = cv.ConvoyId AND m.Vin = cv.Vin AND m.Status IN {FinishedStatuses})
             """,
            new { convoyId },
            transaction,
            cancellationToken: cancellationToken));

        if (stillTravelling > 0)
        {
            // Disposing the transaction without committing rolls the ArrivedAt stamp back.
            return ArriveResult.VehiclesStillTravelling;
        }

        // A vehicle is itself part of the aid: Delivered and Lost ones stay in Ukraine and are
        // never offered for a convoy again. A withdrawn vehicle is skipped — it broke down and
        // left, so whatever happened to it did not happen here.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE v SET HandedOverAt = @arrivedAt, UpdatedAt = SYSUTCDATETIME()
            FROM dbo.Vehicle AS v
            INNER JOIN dbo.ConvoyVehicle AS cv ON cv.Vin = v.Vin AND cv.ConvoyId = @convoyId
            WHERE cv.WithdrawnAt IS NULL
              AND v.HandedOverAt IS NULL
              AND EXISTS (SELECT 1 FROM dbo.Manifest AS m
                          WHERE m.ConvoyId = @convoyId AND m.Vin = v.Vin AND m.Status IN (@delivered, @lost));
            """,
            new
            {
                convoyId,
                arrivedAt,
                delivered = (int)ManifestStatus.Delivered,
                lost = (int)ManifestStatus.Lost,
            },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return ArriveResult.Arrived;
    }

    public async Task<IReadOnlyList<string>> ListVehiclesStillTravellingAsync(int convoyId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            $"""
             SELECT cv.Vin FROM dbo.ConvoyVehicle AS cv
             WHERE cv.ConvoyId = @convoyId
               AND cv.WithdrawnAt IS NULL
               AND NOT EXISTS (SELECT 1 FROM dbo.Manifest AS m
                               WHERE m.ConvoyId = cv.ConvoyId AND m.Vin = cv.Vin AND m.Status IN {FinishedStatuses})
             ORDER BY cv.Vin
             """,
            new { convoyId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }
}
