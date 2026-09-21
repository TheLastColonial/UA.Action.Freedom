using Dapper;
using Microsoft.Data.SqlClient;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Vehicles;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Data.Vehicles;

/// <summary>
/// Dapper-backed <see cref="IVehicleRepository"/> over <c>dbo.Vehicle</c>. Every statement is
/// parameterised; the write methods return the affected-row count as a bool so the handlers
/// can tell "no such VIN" from "done".
///
/// The inspection pair is read here but never written by an add or an edit — only
/// <see cref="RecordInspectionAsync"/> writes it — and <c>ConvoyId</c> is not a column at all any
/// more. Which convoy a vehicle is travelling with is the truck list, <c>dbo.ConvoyVehicle</c>,
/// and it is derived below rather than stored, so it cannot drift from the list.
/// </summary>
public sealed class VehicleRepository(IDbConnectionFactory connectionFactory) : IVehicleRepository
{
    /// <summary>
    /// The convoy this vehicle is currently travelling with, or NULL. Derived, not stored: a
    /// stored pointer had to be cleared at arrival, which is how an arrived convoy came to lose
    /// its own truck list.
    /// </summary>
    private const string CurrentConvoy =
        """
        (SELECT TOP 1 cv.ConvoyId
         FROM dbo.ConvoyVehicle AS cv
         INNER JOIN dbo.Convoy AS c ON c.Id = cv.ConvoyId
         WHERE cv.Vin = v.Vin AND cv.WithdrawnAt IS NULL AND c.ArrivedAt IS NULL
         ORDER BY cv.AddedAt DESC) AS ConvoyId
        """;

    private static readonly string Columns =
        $"v.Vin, v.Plate, v.Brand, v.Model, v.Colour, v.Transmission, v.Notes, v.Mileage, v.Servicing, v.[Year], v.Fuel, {CurrentConvoy}, v.PurchaserName, v.PurchaseDate, v.WeightKg, v.MaxCargoWeightKg, v.CargoWidthCm, v.CargoDepthCm, v.CargoHeightCm, v.InspectionStatus, v.InspectionNotes, v.HandedOverAt";

    public async Task<VehicleReadModel?> GetByVinAsync(string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<VehicleReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM dbo.Vehicle AS v WHERE v.Vin = @vin",
            new { vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<VehicleReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<VehicleReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM dbo.Vehicle AS v ORDER BY v.Vin OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY",
            new { skip = (page - 1) * pageSize, take = pageSize },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<bool> ExistsAsync(string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.Vehicle WHERE Vin = @vin",
            new { vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));

        return count > 0;
    }

    public async Task AddAsync(VehicleReadModel vehicle, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.Vehicle
                (Vin, Plate, Brand, Model, Colour, Transmission, Notes, Mileage, Servicing, [Year], Fuel, PurchaserName, PurchaseDate, WeightKg, MaxCargoWeightKg, CargoWidthCm, CargoDepthCm, CargoHeightCm)
            VALUES
                (@Vin, @Plate, @Brand, @Model, @Colour, @Transmission, @Notes, @Mileage, @Servicing, @Year, @Fuel, @PurchaserName, @PurchaseDate, @WeightKg, @MaxCargoWeightKg, @CargoWidthCm, @CargoDepthCm, @CargoHeightCm)
            """,
            vehicle,
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(VehicleReadModel vehicle, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Vehicle SET
                Plate = @Plate,
                Brand = @Brand,
                Model = @Model,
                Colour = @Colour,
                Transmission = @Transmission,
                Notes = @Notes,
                Mileage = @Mileage,
                Servicing = @Servicing,
                [Year] = @Year,
                Fuel = @Fuel,
                PurchaserName = @PurchaserName,
                PurchaseDate = @PurchaseDate,
                WeightKg = @WeightKg,
                MaxCargoWeightKg = @MaxCargoWeightKg,
                CargoWidthCm = @CargoWidthCm,
                CargoDepthCm = @CargoDepthCm,
                CargoHeightCm = @CargoHeightCm,
                UpdatedAt = SYSUTCDATETIME()
            WHERE Vin = CAST(@Vin AS varchar(32))
            """,
            vehicle,
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> RecordInspectionAsync(
        string vin, InspectionStatus status, string? notes, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Vehicle SET
                InspectionStatus = @status,
                InspectionNotes = @notes,
                UpdatedAt = SYSUTCDATETIME()
            WHERE Vin = @vin
            """,
            new { vin = SqlKey.Of(vin), status = (int)status, notes },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<DeleteResult> DeleteAsync(string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // FK_Manifest_Vehicle is NO ACTION: a manifest is the record of what the vehicle carried.
        try
        {
            var affected = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.Vehicle WHERE Vin = @vin",
                new { vin = SqlKey.Of(vin) },
                cancellationToken: cancellationToken));

            return affected > 0 ? DeleteResult.Deleted : DeleteResult.NotFound;
        }
        catch (SqlException exception) when (exception.Number == SqlErrors.ForeignKeyViolation)
        {
            return DeleteResult.StillReferenced;
        }
    }
}
