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
/// <c>ConvoyId</c> and the inspection pair are read here but never written by an add or an edit:
/// convoy membership changes only through <c>ConvoyRepository</c>, where the truck-list freeze,
/// the inspection gate and the crew clean-up are, and the inspection only through
/// <see cref="RecordInspectionAsync"/>.
/// </summary>
public sealed class VehicleRepository(IDbConnectionFactory connectionFactory) : IVehicleRepository
{
    private const string Columns =
        "Vin, Plate, Brand, Model, Colour, Transmission, Notes, Mileage, Servicing, [Year], Fuel, ConvoyId, PurchaserName, PurchaseDate, WeightKg, MaxCargoWeightKg, CargoWidthCm, CargoDepthCm, CargoHeightCm, InspectionStatus, InspectionNotes, HandedOverAt";

    public async Task<VehicleReadModel?> GetByVinAsync(string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<VehicleReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM dbo.Vehicle WHERE Vin = @vin",
            new { vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<VehicleReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<VehicleReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM dbo.Vehicle ORDER BY Vin OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY",
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
