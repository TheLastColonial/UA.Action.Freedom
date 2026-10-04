using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Data.Convoys;

/// <summary>
/// Dapper-backed <see cref="IConvoyVehicleRepository"/> over <c>dbo.ConvoyVehicle</c> — the truck
/// list — and the two tables hanging off it, <c>dbo.ConvoyVehicleCrew</c> and
/// <c>dbo.ConvoyVehicleInsurance</c>.
/// </summary>
/// <remarks>
/// Every VIN goes through <see cref="SqlKey.Of"/>, or an inline <c>CAST(@Vin AS varchar(32))</c>
/// where a whole record is the parameter object. Dapper sends a .NET string as
/// <c>nvarchar(4000)</c>, and under the database's collation that converts the <em>column</em>, so
/// an unmarked <c>WHERE Vin = @vin</c> scans and locks every row it reads.
/// </remarks>
public sealed class ConvoyVehicleRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution)
    : IConvoyVehicleRepository
{
    private const int PrimaryKeyViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    /// <summary>
    /// The truck-list row with its vehicle, and the crew counted per role in one pass.
    /// </summary>
    /// <remarks>
    /// Conditional aggregates rather than two correlated subqueries: the counts come from
    /// one scan of the crew rows for the vehicle, and the shape lines up with
    /// <see cref="ConvoyVehicleReadModel"/>'s primary constructor for Dapper to hydrate.
    /// </remarks>
    private const string ListSelect =
        """
        SELECT
            cv.Vin,
            v.Plate,
            v.WeightKg,
            COALESCE(c.Drivers, 0)    AS DriverCount,
            COALESCE(c.Passengers, 0) AS PassengerCount,
            cv.WithdrawnAt,
            cv.WithdrawnReason,
            cv.HandoverReceiverRef
        FROM dbo.ConvoyVehicle AS cv
        INNER JOIN dbo.Vehicle AS v ON v.Vin = cv.Vin
        OUTER APPLY (
            SELECT
                SUM(CASE WHEN crew.[Role] = @driver    THEN 1 ELSE 0 END) AS Drivers,
                SUM(CASE WHEN crew.[Role] = @passenger THEN 1 ELSE 0 END) AS Passengers
            FROM dbo.ConvoyVehicleCrew AS crew
            WHERE crew.ConvoyId = cv.ConvoyId AND crew.Vin = cv.Vin
        ) AS c
        """;

    private static object CrewRoleParameters(int convoyId) => new
    {
        convoyId,
        driver = (int)CrewRole.Driver,
        passenger = (int)CrewRole.Passenger,
    };

    public async Task<IReadOnlyList<ConvoyVehicleReadModel>> ListAsync(int convoyId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Withdrawn vehicles are included: the truck list is the record of what set off, not only
        // of what is still moving, and each row says which it is.
        var rows = await connection.QueryAsync<ConvoyVehicleReadModel>(new CommandDefinition(
            $"{ListSelect} WHERE cv.ConvoyId = @convoyId ORDER BY cv.Vin",
            CrewRoleParameters(convoyId),
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<ConvoyVehicleReadModel?> GetAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var parameters = new DynamicParameters(CrewRoleParameters(convoyId));
        parameters.Add("vin", SqlKey.Of(vin));

        return await connection.QuerySingleOrDefaultAsync<ConvoyVehicleReadModel>(new CommandDefinition(
            $"{ListSelect} WHERE cv.ConvoyId = @convoyId AND cv.Vin = @vin",
            parameters,
            cancellationToken: cancellationToken));
    }

    public async Task<AddToTruckListResult> AddAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // The rules are part of the INSERT, so the database settles the race rather than a
        // read-then-write here. "Not on another convoy" means: no un-withdrawn row on a convoy
        // that has not arrived — an arrived convoy releases its vehicles by having arrived, and a
        // vehicle that broke down and left is free to be put on the next one.
        var inserted = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.ConvoyVehicle (ConvoyId, Vin, LastChangedBy, LastChangedAt)
            SELECT @convoyId, v.Vin, @changedBy, SYSUTCDATETIME()
            FROM dbo.Vehicle AS v
            WHERE v.Vin = @vin
              AND v.InspectionStatus = @passed
              AND v.HandedOverAt IS NULL
              AND NOT EXISTS (SELECT 1 FROM dbo.ConvoyVehicle AS cv
                              INNER JOIN dbo.Convoy AS c ON c.Id = cv.ConvoyId
                              WHERE cv.Vin = v.Vin
                                AND cv.WithdrawnAt IS NULL
                                AND c.ArrivedAt IS NULL
                                AND cv.ConvoyId <> @convoyId)
              AND NOT EXISTS (SELECT 1 FROM dbo.ConvoyVehicle AS mine
                              WHERE mine.ConvoyId = @convoyId AND mine.Vin = v.Vin)
            """,
            attribution.With(new { convoyId, vin = SqlKey.Of(vin), passed = (int)InspectionStatus.Passed }),
            cancellationToken: cancellationToken));

        return inserted > 0
            ? AddToTruckListResult.Added
            : await WhyNotAddedAsync(connection, convoyId, vin, cancellationToken);
    }

    /// <summary>Only reached when the conditional insert matched nothing; says which rule held.</summary>
    private static async Task<AddToTruckListResult> WhyNotAddedAsync(
        DbConnection connection, int convoyId, string vin, CancellationToken cancellationToken)
    {
        var vehicle = await connection.QuerySingleOrDefaultAsync<VehicleEligibility>(new CommandDefinition(
            """
            SELECT
                v.InspectionStatus,
                CAST(CASE WHEN v.HandedOverAt IS NULL THEN 0 ELSE 1 END AS bit) AS HandedOver,
                CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.ConvoyVehicle AS mine
                                       WHERE mine.ConvoyId = @convoyId AND mine.Vin = v.Vin)
                          THEN 1 ELSE 0 END AS bit) AS OnThisConvoy,
                CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.ConvoyVehicle AS cv
                                       INNER JOIN dbo.Convoy AS c ON c.Id = cv.ConvoyId
                                       WHERE cv.Vin = v.Vin AND cv.WithdrawnAt IS NULL
                                         AND c.ArrivedAt IS NULL AND cv.ConvoyId <> @convoyId)
                          THEN 1 ELSE 0 END AS bit) AS OnAnotherConvoy
            FROM dbo.Vehicle AS v
            WHERE v.Vin = @vin
            """,
            new { convoyId, vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));

        return vehicle switch
        {
            null => AddToTruckListResult.VehicleNotFound,
            { OnThisConvoy: true } => AddToTruckListResult.AlreadyOnThisConvoy,
            { HandedOver: true } => AddToTruckListResult.HandedOver,
            { InspectionStatus: not (int)InspectionStatus.Passed } => AddToTruckListResult.NotPassedInspection,
            { OnAnotherConvoy: true } => AddToTruckListResult.OnAnotherConvoy,
            _ => AddToTruckListResult.VehicleNotFound,
        };
    }

    private sealed record VehicleEligibility(
        int InspectionStatus, bool HandedOver, bool OnThisConvoy, bool OnAnotherConvoy);

    public async Task<bool> RemoveAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // The crew and the insurance cascade from the truck-list row, so one statement is enough —
        // this is what the old two-step-in-a-transaction was compensating for when the link was a
        // column on dbo.Vehicle and nothing owned the pair.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.ConvoyVehicle WHERE ConvoyId = @convoyId AND Vin = @vin",
            new { convoyId, vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> SetHandoverReceiverAsync(
        int convoyId, string vin, Guid receiverRef, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // A withdrawn vehicle is not handed over by this convoy, so the write is conditional on it
        // still travelling, like the crew and the insurance.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ConvoyVehicle SET
                HandoverReceiverRef = @receiverRef,
                LastChangedBy = @changedBy,
                LastChangedAt = SYSUTCDATETIME()
            WHERE ConvoyId = @convoyId AND Vin = @vin AND WithdrawnAt IS NULL
            """,
            attribution.With(new { convoyId, vin = SqlKey.Of(vin), receiverRef }),
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> WithdrawAsync(
        int convoyId, string vin, string? reason, DateTime withdrawnAt, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Conditional on the vehicle not having withdrawn already, so two dispatchers recording
        // the same breakdown resolve to one withdrawal with one timestamp.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ConvoyVehicle SET WithdrawnAt = @withdrawnAt, WithdrawnReason = @reason, LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
            WHERE ConvoyId = @convoyId AND Vin = @vin AND WithdrawnAt IS NULL
            """,
            attribution.With(new { convoyId, vin = SqlKey.Of(vin), reason, withdrawnAt }),
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<IReadOnlyList<VehicleCrewReadModel>?> ListCrewAsync(
        int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var onThisConvoy = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT CAST(CASE WHEN COUNT(1) > 0 THEN 1 ELSE 0 END AS bit)
            FROM dbo.ConvoyVehicle WHERE ConvoyId = @convoyId AND Vin = @vin
            """,
            new { convoyId, vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));

        if (!onThisConvoy)
        {
            return null;
        }

        var rows = await connection.QueryAsync<VehicleCrewReadModel>(new CommandDefinition(
            """
            SELECT
                crew.PersonId,
                person.FirstName,
                person.LastName,
                crew.[Role]
            FROM dbo.ConvoyVehicleCrew AS crew
            -- An erased volunteer keeps their seat in the history, but not their name: the view says "Former volunteer".
            INNER JOIN dbo.PersonDisplay AS person ON person.PersonId = crew.PersonId
            WHERE crew.ConvoyId = @convoyId AND crew.Vin = @vin
            ORDER BY crew.[Role], LastName, FirstName
            """,
            new { convoyId, vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<AssignCrewResult> AssignCrewAsync(
        int convoyId, string vin, Guid personId, CrewRole role, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // One conditional INSERT rather than check-then-insert: the vehicle has to be on this
        // convoy and still travelling with it, and the person in no other seat on this convoy, at
        // the moment the row is written. UQ_ConvoyVehicleCrew_Convoy_Person settles a race the
        // WHERE cannot see. The insurance is untouched: a driver added here is uncovered until the
        // policy is recorded again, which is derived from their having no covered-driver row.
        try
        {
            var inserted = await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.ConvoyVehicleCrew (ConvoyId, Vin, PersonId, [Role], LastChangedBy, LastChangedAt)
                SELECT @convoyId, @vin, @personId, @role, @changedBy, SYSUTCDATETIME()
                WHERE EXISTS (SELECT 1 FROM dbo.ConvoyVehicle
                              WHERE ConvoyId = @convoyId AND Vin = @vin AND WithdrawnAt IS NULL)
                  AND NOT EXISTS (SELECT 1 FROM dbo.ConvoyVehicleCrew
                                  WHERE ConvoyId = @convoyId AND PersonId = @personId)
                """,
                attribution.With(new { convoyId, vin = SqlKey.Of(vin), personId, role = (int)role }),
                cancellationToken: cancellationToken));

            if (inserted > 0)
            {
                return AssignCrewResult.Assigned;
            }
        }
        catch (SqlException exception) when (exception.Number is PrimaryKeyViolation or UniqueIndexViolation)
        {
            // Lost a race with a second dispatcher; the read below says which seat won.
        }

        return await WhyNotSeatedAsync(connection, convoyId, vin, personId, cancellationToken);
    }

    private static async Task<AssignCrewResult> WhyNotSeatedAsync(
        DbConnection connection, int convoyId, string vin, Guid personId, CancellationToken cancellationToken)
    {
        var seatedIn = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT Vin FROM dbo.ConvoyVehicleCrew WHERE ConvoyId = @convoyId AND PersonId = @personId",
            new { convoyId, personId },
            cancellationToken: cancellationToken));

        if (seatedIn is not null)
        {
            return string.Equals(seatedIn, vin, StringComparison.OrdinalIgnoreCase)
                ? AssignCrewResult.AlreadyAssigned
                : AssignCrewResult.OnAnotherVehicle;
        }

        return AssignCrewResult.VehicleNotOnConvoy;
    }

    public async Task<bool> UnassignCrewAsync(
        int convoyId, string vin, Guid personId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM dbo.ConvoyVehicleCrew
            WHERE ConvoyId = @convoyId AND Vin = @vin AND PersonId = @personId
            """,
            new { convoyId, vin = SqlKey.Of(vin), personId },
            transaction,
            cancellationToken: cancellationToken));

        // The policy stays in cover for everybody else; only this person stops being named on it.
        if (affected > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM dbo.ConvoyVehicleInsuranceDriver
                WHERE ConvoyId = @convoyId AND Vin = @vin AND PersonId = @personId
                """,
                new { convoyId, vin = SqlKey.Of(vin), personId },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return affected > 0;
    }

    public async Task<VehicleInsuranceReadModel?> GetInsuranceAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var policy = await connection.QuerySingleOrDefaultAsync<VehicleInsuranceReadModel>(new CommandDefinition(
            """
            SELECT ConvoyId, Vin, Insurer, PolicyNumber, CoverStart, CoverEnd, CostGbp,
                   RecordedByPersonId AS RecordedBy, RecordedAt, VoidedAt
            FROM dbo.ConvoyVehicleInsurance
            WHERE ConvoyId = @convoyId AND Vin = @vin
            """,
            new { convoyId, vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));

        if (policy is null)
        {
            return null;
        }

        // A driver on the crew with no row on the policy was added after it was recorded.
        var uncovered = await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT crew.PersonId
            FROM dbo.ConvoyVehicleCrew AS crew
            WHERE crew.ConvoyId = @convoyId AND crew.Vin = @vin AND crew.[Role] = @driver
              AND NOT EXISTS (SELECT 1 FROM dbo.ConvoyVehicleInsuranceDriver AS covered
                              WHERE covered.ConvoyId = crew.ConvoyId AND covered.Vin = crew.Vin
                                AND covered.PersonId = crew.PersonId)
            ORDER BY crew.PersonId
            """,
            new { convoyId, vin = SqlKey.Of(vin), driver = (int)CrewRole.Driver },
            cancellationToken: cancellationToken));

        return policy with { UncoveredDrivers = uncovered.ToList() };
    }

    public async Task<bool> RecordInsuranceAsync(VehicleInsuranceRecord insurance, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Replace rather than accumulate: the latest policy is the one that covers the drivers. The
        // vehicle has to be travelling with the convoy at the moment it is written — insuring one
        // that has broken down and left is buying cover for a journey it is not making.
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE dbo.ConvoyVehicleInsurance WITH (HOLDLOCK) AS target
            USING (SELECT @ConvoyId AS ConvoyId, CAST(@Vin AS varchar(32)) AS Vin
                   WHERE EXISTS (SELECT 1 FROM dbo.ConvoyVehicle
                                 WHERE ConvoyId = @ConvoyId
                                   AND Vin = CAST(@Vin AS varchar(32))
                                   AND WithdrawnAt IS NULL)) AS source
            ON target.ConvoyId = source.ConvoyId AND target.Vin = source.Vin
            WHEN MATCHED THEN UPDATE SET
                Insurer = @Insurer, PolicyNumber = @PolicyNumber, CoverStart = @CoverStart,
                CoverEnd = @CoverEnd, CostGbp = @CostGbp, RecordedByPersonId = @RecordedBy,
                RecordedAt = SYSUTCDATETIME(), VoidedAt = NULL
            WHEN NOT MATCHED THEN INSERT
                (ConvoyId, Vin, Insurer, PolicyNumber, CoverStart, CoverEnd, CostGbp, RecordedByPersonId)
                VALUES (@ConvoyId, @Vin, @Insurer, @PolicyNumber, @CoverStart, @CoverEnd, @CostGbp, @RecordedBy);
            """,
            insurance,
            transaction,
            cancellationToken: cancellationToken));

        if (affected > 0)
        {
            // Recording names every driver the vehicle has right now.
            await connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM dbo.ConvoyVehicleInsuranceDriver
                WHERE ConvoyId = @ConvoyId AND Vin = CAST(@Vin AS varchar(32));

                INSERT INTO dbo.ConvoyVehicleInsuranceDriver (ConvoyId, Vin, PersonId)
                SELECT ConvoyId, Vin, PersonId
                FROM dbo.ConvoyVehicleCrew
                WHERE ConvoyId = @ConvoyId AND Vin = CAST(@Vin AS varchar(32)) AND [Role] = @driver;
                """,
                new { insurance.ConvoyId, insurance.Vin, driver = (int)CrewRole.Driver },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return affected > 0;
    }

    public async Task<bool> RemoveInsuranceAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.ConvoyVehicleInsurance WHERE ConvoyId = @convoyId AND Vin = @vin",
            new { convoyId, vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<FerryBookingReadModel?> GetFerryBookingAsync(
        int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<FerryBookingReadModel>(new CommandDefinition(
            $"""
            SELECT f.ConvoyId, f.Vin, f.Operator, f.Reference, f.SailingAt, f.TicketDetails, f.CostGbp,
                   {ChangeStamp.ReadColumns("f")}
            FROM dbo.ConvoyVehicleFerryBooking AS f {ChangeStamp.ReadJoin("f")}
            WHERE f.ConvoyId = @convoyId AND f.Vin = @vin
            """,
            new { convoyId, vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> RecordFerryBookingAsync(FerryBookingRecord booking, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Replace rather than accumulate, and only while the vehicle is travelling with the
        // convoy: the MERGE source is empty for a withdrawn or unlisted vehicle, so nothing is written.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE dbo.ConvoyVehicleFerryBooking WITH (HOLDLOCK) AS target
            USING (SELECT @ConvoyId AS ConvoyId, CAST(@Vin AS varchar(32)) AS Vin
                   WHERE EXISTS (SELECT 1 FROM dbo.ConvoyVehicle
                                 WHERE ConvoyId = @ConvoyId
                                   AND Vin = CAST(@Vin AS varchar(32))
                                   AND WithdrawnAt IS NULL)) AS source
            ON target.ConvoyId = source.ConvoyId AND target.Vin = source.Vin
            WHEN MATCHED THEN UPDATE SET
                Operator = @Operator, Reference = @Reference, SailingAt = @SailingAt,
                TicketDetails = @TicketDetails, CostGbp = @CostGbp,
                LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (ConvoyId, Vin, Operator, Reference, SailingAt, TicketDetails, CostGbp, LastChangedBy, LastChangedAt)
                VALUES (@ConvoyId, @Vin, @Operator, @Reference, @SailingAt, @TicketDetails, @CostGbp,
                        @changedBy, SYSUTCDATETIME());
            """,
            attribution.With(booking),
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> RemoveFerryBookingAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.ConvoyVehicleFerryBooking WHERE ConvoyId = @convoyId AND Vin = @vin",
            new { convoyId, vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<IReadOnlyList<ManifestBoxReadModel>?> ListBoxesAsync(
        int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var onThisConvoy = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT CAST(CASE WHEN COUNT(1) > 0 THEN 1 ELSE 0 END AS bit)
            FROM dbo.ConvoyVehicle WHERE ConvoyId = @convoyId AND Vin = @vin
            """,
            new { convoyId, vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));

        if (!onThisConvoy)
        {
            return null;
        }

        // The box's own weight and validation state come along, because a weight built from
        // anything else would be a number nobody had confirmed.
        var rows = await connection.QueryAsync<ManifestBoxReadModel>(new CommandDefinition(
            """
            SELECT b.Id AS BoxId,
                   b.WeightKg,
                   CAST(CASE WHEN b.ValidatedAt IS NULL THEN 0 ELSE 1 END AS bit) AS Validated,
                   b.WidthCm,
                   b.DepthCm,
                   b.HeightCm
            FROM dbo.ConvoyVehicleBoxAllocation AS a
            INNER JOIN dbo.Box AS b ON b.Id = a.BoxId
            WHERE a.ConvoyId = @convoyId AND a.Vin = @vin
            ORDER BY b.Id
            """,
            new { convoyId, vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<BoxAllocation?> GetBoxAllocationAsync(int boxId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var row = await connection.QuerySingleOrDefaultAsync<AllocationRow>(new CommandDefinition(
            "SELECT ConvoyId, Vin, AllocatedAt FROM dbo.ConvoyVehicleBoxAllocation WHERE BoxId = @boxId",
            new { boxId },
            cancellationToken: cancellationToken));

        return row is null ? null : new BoxAllocation(new ConvoyId(row.ConvoyId), row.Vin, boxId, row.AllocatedAt);
    }

    public async Task<AllocateBoxResult> AllocateBoxAsync(
        int convoyId, string vin, int boxId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // One transaction because the entry's state and the box's allocation are read together:
        // UPDLOCK on the entry makes a concurrent withdrawal wait, and HOLDLOCK on the allocation
        // makes two dispatchers racing for the same unallocated box take turns, so the primary key
        // is the backstop and not the mechanism.
        var entry = await connection.QuerySingleOrDefaultAsync<EntryState>(new CommandDefinition(
            """
            SELECT CAST(CASE WHEN WithdrawnAt IS NULL THEN 0 ELSE 1 END AS bit) AS Withdrawn
            FROM dbo.ConvoyVehicle WITH (UPDLOCK)
            WHERE ConvoyId = @convoyId AND Vin = @vin
            """,
            new { convoyId, vin = SqlKey.Of(vin) },
            transaction,
            cancellationToken: cancellationToken));

        if (entry is null)
        {
            return AllocateBoxResult.VehicleNotOnConvoy;
        }

        if (entry.Withdrawn)
        {
            return AllocateBoxResult.VehicleWithdrawn;
        }

        var boxExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Box WHERE Id = @boxId) THEN 1 ELSE 0 END AS bit)",
            new { boxId },
            transaction,
            cancellationToken: cancellationToken));

        if (!boxExists)
        {
            return AllocateBoxResult.BoxNotFound;
        }

        var current = await connection.QuerySingleOrDefaultAsync<AllocationRow>(new CommandDefinition(
            """
            SELECT ConvoyId, Vin, AllocatedAt
            FROM dbo.ConvoyVehicleBoxAllocation WITH (UPDLOCK, HOLDLOCK)
            WHERE BoxId = @boxId
            """,
            new { boxId },
            transaction,
            cancellationToken: cancellationToken));

        if (current is not null
            && current.ConvoyId == convoyId
            && string.Equals(current.Vin, vin, StringComparison.OrdinalIgnoreCase))
        {
            return AllocateBoxResult.AlreadyAllocated;
        }

        var sql = current is null
            ? """
              INSERT INTO dbo.ConvoyVehicleBoxAllocation (BoxId, ConvoyId, Vin, LastChangedBy, LastChangedAt)
              VALUES (@boxId, @convoyId, @vin, @changedBy, SYSUTCDATETIME())
              """
            : """
              UPDATE dbo.ConvoyVehicleBoxAllocation SET
                  ConvoyId = @convoyId,
                  Vin = @vin,
                  AllocatedAt = SYSUTCDATETIME(),
                  LastChangedBy = @changedBy,
                  LastChangedAt = SYSUTCDATETIME()
              WHERE BoxId = @boxId
              """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            attribution.With(new { convoyId, vin = SqlKey.Of(vin), boxId }),
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

        return current is null ? AllocateBoxResult.Allocated : AllocateBoxResult.Moved;
    }

    public async Task<bool> RemoveBoxAsync(int convoyId, string vin, int boxId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Scoped to this vehicle: taking a box off one it was never on is a caller mistake.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.ConvoyVehicleBoxAllocation WHERE BoxId = @boxId AND ConvoyId = @convoyId AND Vin = @vin",
            new { boxId, convoyId, vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    private sealed record AllocationRow(int ConvoyId, string Vin, DateTime AllocatedAt);

    private sealed record EntryState(bool Withdrawn);
}
