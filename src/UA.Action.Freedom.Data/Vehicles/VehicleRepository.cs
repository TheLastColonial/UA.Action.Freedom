using Dapper;
using UA.Action.Freedom.Application.Vehicles;

namespace UA.Action.Freedom.Data.Vehicles;

/// <summary>
/// Dapper-backed <see cref="IVehicleRepository"/> over <c>dbo.Vehicle</c>. Every statement is
/// parameterised; the write methods return the affected-row count as a bool so the handlers
/// can tell "no such VIN" from "done".
/// </summary>
public sealed class VehicleRepository(IDbConnectionFactory connectionFactory) : IVehicleRepository
{
    private const string Columns =
        "Vin, Plate, Brand, Model, Colour, Transmission, Notes, Mileage, Servicing, [Year], Fuel, ConvoyId, PurchaserName, PurchaseDate, WeightKg";

    public async Task<VehicleReadModel?> GetByVinAsync(string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<VehicleReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM dbo.Vehicle WHERE Vin = @vin",
            new { vin },
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
            new { vin },
            cancellationToken: cancellationToken));

        return count > 0;
    }

    public async Task AddAsync(VehicleReadModel vehicle, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.Vehicle
                (Vin, Plate, Brand, Model, Colour, Transmission, Notes, Mileage, Servicing, [Year], Fuel, ConvoyId, PurchaserName, PurchaseDate, WeightKg)
            VALUES
                (@Vin, @Plate, @Brand, @Model, @Colour, @Transmission, @Notes, @Mileage, @Servicing, @Year, @Fuel, @ConvoyId, @PurchaserName, @PurchaseDate, @WeightKg)
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
                ConvoyId = @ConvoyId,
                PurchaserName = @PurchaserName,
                PurchaseDate = @PurchaseDate,
                WeightKg = @WeightKg,
                UpdatedAt = SYSUTCDATETIME()
            WHERE Vin = @Vin
            """,
            vehicle,
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> DeleteAsync(string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.Vehicle WHERE Vin = @vin",
            new { vin },
            cancellationToken: cancellationToken));

        return affected > 0;
    }
}
