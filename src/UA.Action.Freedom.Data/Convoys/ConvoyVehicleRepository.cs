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
            cv.WithdrawnReason
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
                COALESCE(d.FirstName, N'Former') AS FirstName,
                COALESCE(d.LastName, N'volunteer') AS LastName,
                crew.[Role]
            FROM dbo.ConvoyVehicleCrew AS crew
            -- LEFT: an erased volunteer keeps their seat in the history, but not their name.
            LEFT JOIN dbo.PersonDetail AS d ON d.PersonId = crew.PersonId
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
                INSERT INTO dbo.ConvoyVehicleCrew (ConvoyId, Vin, PersonId, [Role])
                SELECT @convoyId, @vin, @personId, @role
                WHERE EXISTS (SELECT 1 FROM dbo.ConvoyVehicle
                              WHERE ConvoyId = @convoyId AND Vin = @vin AND WithdrawnAt IS NULL)
                  AND NOT EXISTS (SELECT 1 FROM dbo.ConvoyVehicleCrew
                                  WHERE ConvoyId = @convoyId AND PersonId = @personId)
                """,
                new { convoyId, vin = SqlKey.Of(vin), personId, role = (int)role },
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
}
