using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Data.Convoys;

/// <summary>
/// Dapper-backed <see cref="IConvoyRepository"/> over <c>dbo.Convoy</c>,
/// <c>dbo.ConvoyRouteStop</c> and the <c>ConvoyId</c> column of <c>dbo.Vehicle</c>. Every
/// statement is parameterised; the write methods return the affected-row count as a bool so
/// the handlers can tell "no such row" from "done".
/// </summary>
public sealed class ConvoyRepository(IDbConnectionFactory connectionFactory) : IConvoyRepository
{
    private const string Columns = "Id, Start, ExpectedEnd, TruckListPublishedAt, ArrivedAt";

    /// <summary>A manifest in one of these says what became of its vehicle; the journey is over for it.</summary>
    private static readonly string FinishedStatuses =
        $"({(int)ManifestStatus.Delivered}, {(int)ManifestStatus.Lost}, {(int)ManifestStatus.Returned})";

    private const string StopColumns = "Sequence, House, Street, City, Country, Postcode";

    private const int PrimaryKeyViolation = 2627;
    private const int UniqueIndexViolation = 2601;

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
        await connection.OpenAsync(cancellationToken);

        // Route stops cascade and vehicles are released to ConvoyId NULL by the foreign key — but
        // the crew does not cascade (a second cascade path from Convoy is not allowed, see the
        // schema), and FK_VehicleDriver_Convoy would refuse the delete while crew remain. Same
        // transaction as the delete, as in UnassignVehicleAsync.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM dbo.VehicleDriver WHERE ConvoyId = @id;
            DELETE FROM dbo.VehicleInsurance WHERE ConvoyId = @id;
            """,
            new { id },
            transaction,
            cancellationToken: cancellationToken));

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.Convoy WHERE Id = @id",
            new { id },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

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

    public async Task<IReadOnlyList<ConvoyVehicleReadModel>> ListVehiclesAsync(
        int convoyId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<ConvoyVehicleReadModel>(new CommandDefinition(
            """
            SELECT
                v.Vin,
                v.Plate,
                v.WeightKg,
                (SELECT COUNT(1) FROM dbo.VehicleDriver AS vd
                 WHERE vd.ConvoyId = @convoyId AND vd.Vin = v.Vin AND vd.[Role] = @driver) AS DriverCount,
                (SELECT COUNT(1) FROM dbo.VehicleDriver AS vd
                 WHERE vd.ConvoyId = @convoyId AND vd.Vin = v.Vin AND vd.[Role] = @passenger) AS PassengerCount
            FROM dbo.Vehicle v
            WHERE v.ConvoyId = @convoyId
            ORDER BY v.Vin
            """,
            new { convoyId, driver = (int)CrewRole.Driver, passenger = (int)CrewRole.Passenger },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<AssignVehicleResult> AssignVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // The rules are part of the UPDATE, so the database settles the race rather than a
        // read-then-write here. Only when nothing matched is the row read, to say which rule held.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Vehicle SET ConvoyId = @convoyId, UpdatedAt = SYSUTCDATETIME()
            WHERE Vin = @vin
              AND InspectionStatus = @passed
              AND HandedOverAt IS NULL
              AND (ConvoyId IS NULL OR ConvoyId = @convoyId)
            """,
            new { convoyId, vin, passed = (int)InspectionStatus.Passed },
            cancellationToken: cancellationToken));

        if (affected > 0)
        {
            return AssignVehicleResult.Assigned;
        }

        var vehicle = await connection.QuerySingleOrDefaultAsync<(int InspectionStatus, bool HandedOver)?>(new CommandDefinition(
            """
            SELECT InspectionStatus, CAST(CASE WHEN HandedOverAt IS NULL THEN 0 ELSE 1 END AS bit) AS HandedOver
            FROM dbo.Vehicle WHERE Vin = @vin
            """,
            new { vin },
            cancellationToken: cancellationToken));

        return vehicle switch
        {
            null => AssignVehicleResult.VehicleNotFound,
            { HandedOver: true } => AssignVehicleResult.HandedOver,
            { InspectionStatus: (int)InspectionStatus.Passed } => AssignVehicleResult.OnAnotherConvoy,
            _ => AssignVehicleResult.NotPassedInspection,
        };
    }

    public async Task<bool> UnassignVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        // Unassigning a vehicle from a convoy must also clear its driver assignments, else the
        // vehicle would still show crew from a convoy it's no longer on. Both operations in one
        // transaction ensures that never happens.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Scoped to this convoy: removing a vehicle from a convoy it was never on is a caller
        // mistake worth reporting, not a silent success that clears someone else's truck list.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM dbo.VehicleDriver WHERE ConvoyId = @convoyId AND Vin = @vin;
            DELETE FROM dbo.VehicleInsurance WHERE ConvoyId = @convoyId AND Vin = @vin;
            """,
            new { convoyId, vin },
            transaction,
            cancellationToken: cancellationToken));

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Vehicle SET ConvoyId = NULL, UpdatedAt = SYSUTCDATETIME()
            WHERE Vin = @vin AND ConvoyId = @convoyId
            """,
            new { convoyId, vin },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

        return affected > 0;
    }

    public async Task<IReadOnlyList<VehicleDriverReadModel>?> ListVehicleDriversAsync(
        int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Check that the vehicle is on this convoy before listing its drivers.
        var vehicleOnConvoy = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT CAST(CASE WHEN COUNT(1) > 0 THEN 1 ELSE 0 END AS bit) FROM dbo.Vehicle WHERE Vin = @vin AND ConvoyId = @convoyId",
            new { vin, convoyId },
            cancellationToken: cancellationToken));

        if (!vehicleOnConvoy)
        {
            return null;
        }

        var rows = await connection.QueryAsync<VehicleDriverReadModel>(new CommandDefinition(
            """
            SELECT
                p.Id AS PersonId,
                p.FirstName,
                p.LastName,
                vd.[Role]
            FROM dbo.VehicleDriver vd
            JOIN dbo.Person p ON vd.PersonId = p.Id
            WHERE vd.ConvoyId = @convoyId AND vd.Vin = @vin
            ORDER BY vd.[Role], p.LastName, p.FirstName
            """,
            new { convoyId, vin },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<AssignDriverResult> AssignDriverAsync(
        int convoyId, string vin, Guid personId, CrewRole role, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        // One conditional INSERT rather than check-then-insert: the vehicle has to be on this
        // convoy, and the person in no other seat of it, at the moment the row is written.
        // UQ_VehicleDriver_Convoy_Person settles a race the WHERE cannot see. The crew and the
        // insurance that names it change together, so the void is in the same transaction.
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var inserted = await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.VehicleDriver (ConvoyId, Vin, PersonId, [Role])
                SELECT @convoyId, @vin, @personId, @role
                WHERE EXISTS (SELECT 1 FROM dbo.Vehicle WHERE Vin = @vin AND ConvoyId = @convoyId)
                  AND NOT EXISTS (SELECT 1 FROM dbo.VehicleDriver WHERE ConvoyId = @convoyId AND PersonId = @personId)
                """,
                new { convoyId, vin, personId, role = (int)role },
                transaction,
                cancellationToken: cancellationToken));

            if (inserted > 0)
            {
                await VoidInsuranceAsync(connection, transaction, convoyId, vin, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return AssignDriverResult.Assigned;
            }
        }
        catch (SqlException exception) when (exception.Number is PrimaryKeyViolation or UniqueIndexViolation)
        {
            // Lost a race with a second dispatcher; the read below says which seat won.
        }

        return await WhyNotSeatedAsync(connection, convoyId, vin, personId, cancellationToken);
    }

    private static Task VoidInsuranceAsync(
        DbConnection connection, DbTransaction transaction, int convoyId, string vin, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.VehicleInsurance SET VoidedAt = SYSUTCDATETIME()
            WHERE ConvoyId = @convoyId AND Vin = @vin AND VoidedAt IS NULL
            """,
            new { convoyId, vin },
            transaction,
            cancellationToken: cancellationToken));

    private static async Task<AssignDriverResult> WhyNotSeatedAsync(
        DbConnection connection, int convoyId, string vin, Guid personId, CancellationToken cancellationToken)
    {
        var seatedIn = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT Vin FROM dbo.VehicleDriver WHERE ConvoyId = @convoyId AND PersonId = @personId",
            new { convoyId, personId },
            cancellationToken: cancellationToken));

        if (seatedIn is not null)
        {
            return string.Equals(seatedIn, vin, StringComparison.OrdinalIgnoreCase)
                ? AssignDriverResult.AlreadyAssigned
                : AssignDriverResult.OnAnotherVehicle;
        }

        return AssignDriverResult.VehicleNotOnConvoy;
    }

    public async Task<bool> UnassignDriverAsync(int convoyId, string vin, Guid personId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM dbo.VehicleDriver
            WHERE ConvoyId = @convoyId AND Vin = @vin AND PersonId = @personId
              AND EXISTS (SELECT 1 FROM dbo.Vehicle WHERE Vin = @vin AND ConvoyId = @convoyId)
            """,
            new { convoyId, vin, personId },
            transaction,
            cancellationToken: cancellationToken));

        if (affected > 0)
        {
            await VoidInsuranceAsync(connection, transaction, convoyId, vin, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return affected > 0;
    }

    public async Task<ArriveResult> ArriveAsync(int convoyId, DateTime arrivedAt, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        // Arrival, handover and release are one fact about the journey: a convoy marked arrived
        // with its vehicles still offered for the next one, or the reverse, must not be possible.
        // UPDLOCK on the convoy row makes a second dispatcher wait rather than both succeed.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var stillTravelling = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"""
             SELECT COUNT(1)
             FROM dbo.Vehicle AS v WITH (UPDLOCK)
             WHERE v.ConvoyId = @convoyId
               AND NOT EXISTS (SELECT 1 FROM dbo.Manifest AS m
                               WHERE m.ConvoyId = @convoyId AND m.Vin = v.Vin AND m.Status IN {FinishedStatuses})
             """,
            new { convoyId },
            transaction,
            cancellationToken: cancellationToken));

        if (stillTravelling > 0)
        {
            return ArriveResult.VehiclesStillTravelling;
        }

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

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE v SET HandedOverAt = @arrivedAt, UpdatedAt = SYSUTCDATETIME()
            FROM dbo.Vehicle AS v
            WHERE v.ConvoyId = @convoyId
              AND EXISTS (SELECT 1 FROM dbo.Manifest AS m
                          WHERE m.ConvoyId = @convoyId AND m.Vin = v.Vin AND m.Status IN (@delivered, @lost));

            UPDATE v SET ConvoyId = NULL, UpdatedAt = SYSUTCDATETIME()
            FROM dbo.Vehicle AS v
            WHERE v.ConvoyId = @convoyId AND v.HandedOverAt IS NULL
              AND EXISTS (SELECT 1 FROM dbo.Manifest AS m
                          WHERE m.ConvoyId = @convoyId AND m.Vin = v.Vin AND m.Status = @returned);
            """,
            new
            {
                convoyId,
                arrivedAt,
                delivered = (int)ManifestStatus.Delivered,
                lost = (int)ManifestStatus.Lost,
                returned = (int)ManifestStatus.Returned,
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
             SELECT v.Vin FROM dbo.Vehicle AS v
             WHERE v.ConvoyId = @convoyId
               AND NOT EXISTS (SELECT 1 FROM dbo.Manifest AS m
                               WHERE m.ConvoyId = @convoyId AND m.Vin = v.Vin AND m.Status IN {FinishedStatuses})
             ORDER BY v.Vin
             """,
            new { convoyId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<VehicleInsuranceReadModel?> GetInsuranceAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<VehicleInsuranceReadModel>(new CommandDefinition(
            """
            SELECT ConvoyId, Vin, Insurer, PolicyNumber, CoverStart, CoverEnd, CostGbp,
                   RecordedBySub AS RecordedBy, RecordedAt, VoidedAt
            FROM dbo.VehicleInsurance
            WHERE ConvoyId = @convoyId AND Vin = @vin
            """,
            new { convoyId, vin },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> RecordInsuranceAsync(VehicleInsuranceRecord insurance, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Replace rather than accumulate: the latest policy is the one that covers the crew. The
        // vehicle has to be on the convoy at the moment it is written.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE dbo.VehicleInsurance WITH (HOLDLOCK) AS target
            USING (SELECT @ConvoyId AS ConvoyId, @Vin AS Vin
                   WHERE EXISTS (SELECT 1 FROM dbo.Vehicle WHERE Vin = @Vin AND ConvoyId = @ConvoyId)) AS source
            ON target.ConvoyId = source.ConvoyId AND target.Vin = source.Vin
            WHEN MATCHED THEN UPDATE SET
                Insurer = @Insurer, PolicyNumber = @PolicyNumber, CoverStart = @CoverStart,
                CoverEnd = @CoverEnd, CostGbp = @CostGbp, RecordedBySub = @RecordedBy,
                RecordedAt = SYSUTCDATETIME(), VoidedAt = NULL
            WHEN NOT MATCHED THEN INSERT
                (ConvoyId, Vin, Insurer, PolicyNumber, CoverStart, CoverEnd, CostGbp, RecordedBySub)
                VALUES (@ConvoyId, @Vin, @Insurer, @PolicyNumber, @CoverStart, @CoverEnd, @CostGbp, @RecordedBy);
            """,
            insurance,
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> RemoveInsuranceAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.VehicleInsurance WHERE ConvoyId = @convoyId AND Vin = @vin",
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
