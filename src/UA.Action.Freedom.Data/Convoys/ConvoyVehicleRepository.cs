using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
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
public sealed class ConvoyVehicleRepository(IDbConnectionFactory connectionFactory) : IConvoyVehicleRepository
{
    private const int PrimaryKeyViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    /// <summary>
    /// The truck-list row with its vehicle, and the crew counted per leg and role in one pass.
    /// </summary>
    /// <remarks>
    /// Conditional aggregates rather than four correlated subqueries: the four counts come from
    /// one scan of the crew rows for the convoy, and the shape lines up with
    /// <see cref="ConvoyVehicleReadModel"/>'s primary constructor for Dapper to hydrate.
    /// </remarks>
    private const string ListSelect =
        """
        SELECT
            cv.Vin,
            v.Plate,
            v.WeightKg,
            COALESCE(c.UkDrivers, 0)        AS UkDriverCount,
            COALESCE(c.UkPassengers, 0)     AS UkPassengerCount,
            COALESCE(c.BorderDrivers, 0)    AS BorderDriverCount,
            COALESCE(c.BorderPassengers, 0) AS BorderPassengerCount,
            cv.WithdrawnAt,
            cv.WithdrawnReason
        FROM dbo.ConvoyVehicle AS cv
        INNER JOIN dbo.Vehicle AS v ON v.Vin = cv.Vin
        OUTER APPLY (
            SELECT
                SUM(CASE WHEN crew.Leg = @uk     AND crew.[Role] = @driver    THEN 1 ELSE 0 END) AS UkDrivers,
                SUM(CASE WHEN crew.Leg = @uk     AND crew.[Role] = @passenger THEN 1 ELSE 0 END) AS UkPassengers,
                SUM(CASE WHEN crew.Leg = @border AND crew.[Role] = @driver    THEN 1 ELSE 0 END) AS BorderDrivers,
                SUM(CASE WHEN crew.Leg = @border AND crew.[Role] = @passenger THEN 1 ELSE 0 END) AS BorderPassengers
            FROM dbo.ConvoyVehicleCrew AS crew
            WHERE crew.ConvoyId = cv.ConvoyId AND crew.Vin = cv.Vin
        ) AS c
        """;

    private static object CrewRoleAndLegParameters(int convoyId) => new
    {
        convoyId,
        uk = (int)JourneyLeg.Uk,
        border = (int)JourneyLeg.Border,
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
            CrewRoleAndLegParameters(convoyId),
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<ConvoyVehicleReadModel?> GetAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var parameters = new DynamicParameters(CrewRoleAndLegParameters(convoyId));
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
            INSERT INTO dbo.ConvoyVehicle (ConvoyId, Vin)
            SELECT @convoyId, v.Vin
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
            new { convoyId, vin = SqlKey.Of(vin), passed = (int)InspectionStatus.Passed },
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

    public async Task<bool> WithdrawAsync(
        int convoyId, string vin, string? reason, DateTime withdrawnAt, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Conditional on the vehicle not having withdrawn already, so two dispatchers recording
        // the same breakdown resolve to one withdrawal with one timestamp.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ConvoyVehicle SET WithdrawnAt = @withdrawnAt, WithdrawnReason = @reason
            WHERE ConvoyId = @convoyId AND Vin = @vin AND WithdrawnAt IS NULL
            """,
            new { convoyId, vin = SqlKey.Of(vin), reason, withdrawnAt },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<IReadOnlyList<VehicleCrewReadModel>?> ListCrewAsync(
        int convoyId, string vin, JourneyLeg? leg, CancellationToken cancellationToken)
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
                COALESCE(d.FirstName, N'Former') AS FirstName,
                COALESCE(d.LastName, N'volunteer') AS LastName,
                crew.Leg,
                crew.[Role]
            FROM dbo.ConvoyVehicleCrew AS crew
            -- LEFT: an erased volunteer keeps their seat in the history, but not their name.
            LEFT JOIN dbo.PersonDetail AS d ON d.PersonId = crew.PersonId
            WHERE crew.ConvoyId = @convoyId AND crew.Vin = @vin
              AND (@leg IS NULL OR crew.Leg = @leg)
            ORDER BY crew.Leg, crew.[Role], LastName, FirstName
            """,
            new { convoyId, vin = SqlKey.Of(vin), leg = (int?)leg },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<AssignCrewResult> AssignCrewAsync(
        int convoyId, string vin, Guid personId, JourneyLeg leg, CrewRole role, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        // One conditional INSERT rather than check-then-insert: the vehicle has to be on this
        // convoy and still travelling with it, and the person in no other seat on this leg, at the
        // moment the row is written. UQ_ConvoyVehicleCrew_Convoy_Person_Leg settles a race the
        // WHERE cannot see. The crew and the insurance that names it change together, so the void
        // is in the same transaction.
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var inserted = await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.ConvoyVehicleCrew (ConvoyId, Vin, PersonId, Leg, [Role])
                SELECT @convoyId, @vin, @personId, @leg, @role
                WHERE EXISTS (SELECT 1 FROM dbo.ConvoyVehicle
                              WHERE ConvoyId = @convoyId AND Vin = @vin AND WithdrawnAt IS NULL)
                  AND NOT EXISTS (SELECT 1 FROM dbo.ConvoyVehicleCrew
                                  WHERE ConvoyId = @convoyId AND PersonId = @personId AND Leg = @leg)
                """,
                new { convoyId, vin = SqlKey.Of(vin), personId, leg = (int)leg, role = (int)role },
                transaction,
                cancellationToken: cancellationToken));

            if (inserted > 0)
            {
                await VoidInsuranceAsync(connection, transaction, convoyId, vin, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return AssignCrewResult.Assigned;
            }
        }
        catch (SqlException exception) when (exception.Number is PrimaryKeyViolation or UniqueIndexViolation)
        {
            // Lost a race with a second dispatcher; the read below says which seat won.
        }

        return await WhyNotSeatedAsync(connection, convoyId, vin, personId, leg, cancellationToken);
    }

    /// <summary>
    /// The insurance names the crew, so any change to the crew voids it and it has to be recorded
    /// again before the vehicle departs.
    /// </summary>
    private static Task VoidInsuranceAsync(
        DbConnection connection, DbTransaction transaction, int convoyId, string vin, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ConvoyVehicleInsurance SET VoidedAt = SYSUTCDATETIME()
            WHERE ConvoyId = @convoyId AND Vin = @vin AND VoidedAt IS NULL
            """,
            new { convoyId, vin = SqlKey.Of(vin) },
            transaction,
            cancellationToken: cancellationToken));

    private static async Task<AssignCrewResult> WhyNotSeatedAsync(
        DbConnection connection, int convoyId, string vin, Guid personId, JourneyLeg leg, CancellationToken cancellationToken)
    {
        var seatedIn = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT Vin FROM dbo.ConvoyVehicleCrew WHERE ConvoyId = @convoyId AND PersonId = @personId AND Leg = @leg",
            new { convoyId, personId, leg = (int)leg },
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
        int convoyId, string vin, Guid personId, JourneyLeg leg, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM dbo.ConvoyVehicleCrew
            WHERE ConvoyId = @convoyId AND Vin = @vin AND PersonId = @personId AND Leg = @leg
            """,
            new { convoyId, vin = SqlKey.Of(vin), personId, leg = (int)leg },
            transaction,
            cancellationToken: cancellationToken));

        if (affected > 0)
        {
            await VoidInsuranceAsync(connection, transaction, convoyId, vin, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return affected > 0;
    }

    public async Task<VehicleInsuranceReadModel?> GetInsuranceAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<VehicleInsuranceReadModel>(new CommandDefinition(
            """
            SELECT ConvoyId, Vin, Insurer, PolicyNumber, CoverStart, CoverEnd, CostGbp,
                   RecordedBySub AS RecordedBy, RecordedAt, VoidedAt
            FROM dbo.ConvoyVehicleInsurance
            WHERE ConvoyId = @convoyId AND Vin = @vin
            """,
            new { convoyId, vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> RecordInsuranceAsync(VehicleInsuranceRecord insurance, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Replace rather than accumulate: the latest policy is the one that covers the crew. The
        // vehicle has to be travelling with the convoy at the moment it is written — insuring one
        // that has broken down and left is buying cover for a journey it is not making.
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
            "DELETE FROM dbo.ConvoyVehicleInsurance WHERE ConvoyId = @convoyId AND Vin = @vin",
            new { convoyId, vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));

        return affected > 0;
    }
}
