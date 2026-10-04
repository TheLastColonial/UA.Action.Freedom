using Dapper;
using Microsoft.Data.SqlClient;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Data.Convoys;

/// <summary>
/// Dapper-backed <see cref="IVehicleEquipmentRepository"/> over <c>dbo.EquipmentItem</c> and
/// <c>dbo.ConvoyVehicleEquipment</c>.
/// </summary>
public sealed class VehicleEquipmentRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution)
    : IVehicleEquipmentRepository
{
    private static readonly string LineColumns =
        $"e.Vin, e.EquipmentItemId, i.Name, e.Quantity, i.UnitCostGbp, e.CostGbp, {ChangeStamp.ReadColumns("e")}";

    private static readonly string LineFrom =
        $"dbo.ConvoyVehicleEquipment AS e JOIN dbo.EquipmentItem AS i ON i.Id = e.EquipmentItemId {ChangeStamp.ReadJoin("e")}";

    public async Task<IReadOnlyList<EquipmentItemReadModel>> ListItemsAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<EquipmentItemReadModel>(new CommandDefinition(
            $"""
             SELECT i.Id, i.Name, i.UnitCostGbp, {ChangeStamp.ReadColumns("i")}
             FROM dbo.EquipmentItem AS i {ChangeStamp.ReadJoin("i")}
             ORDER BY i.Name
             """,
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<int?> AddItemAsync(string name, decimal? unitCostGbp, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        try
        {
            return await connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                INSERT INTO dbo.EquipmentItem (Name, UnitCostGbp, LastChangedBy, LastChangedAt)
                OUTPUT INSERTED.Id
                VALUES (@name, @unitCostGbp, @changedBy, SYSUTCDATETIME())
                """,
                attribution.With(new { name, unitCostGbp }),
                cancellationToken: cancellationToken));
        }
        catch (SqlException exception) when (exception.Number is SqlErrors.UniqueIndexViolation or SqlErrors.UniqueConstraintViolation)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<VehicleEquipmentReadModel>> ListForVehicleAsync(
        int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<VehicleEquipmentReadModel>(new CommandDefinition(
            $"""
             SELECT {LineColumns} FROM {LineFrom}
             WHERE e.ConvoyId = @convoyId AND e.Vin = @vin
             ORDER BY i.Name
             """,
            new { convoyId, vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<IReadOnlyList<VehicleEquipmentReadModel>> ListForConvoyAsync(
        int convoyId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<VehicleEquipmentReadModel>(new CommandDefinition(
            $"""
             SELECT {LineColumns} FROM {LineFrom}
             WHERE e.ConvoyId = @convoyId
             ORDER BY e.Vin, i.Name
             """,
            new { convoyId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<ReplaceEquipmentResult> ReplaceForVehicleAsync(
        int convoyId, string vin, IReadOnlyList<VehicleEquipmentLine> lines, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The entry is read under lock so it cannot be removed between this check and the inserts.
        var onConvoy = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.ConvoyVehicle WITH (UPDLOCK) WHERE ConvoyId = @convoyId AND Vin = @vin",
            new { convoyId, vin = SqlKey.Of(vin) },
            transaction,
            cancellationToken: cancellationToken));

        if (onConvoy == 0)
        {
            return ReplaceEquipmentResult.VehicleNotOnConvoy;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.ConvoyVehicleEquipment WHERE ConvoyId = @convoyId AND Vin = @vin",
            new { convoyId, vin = SqlKey.Of(vin) },
            transaction,
            cancellationToken: cancellationToken));

        try
        {
            foreach (var line in lines)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO dbo.ConvoyVehicleEquipment
                        (ConvoyId, Vin, EquipmentItemId, Quantity, CostGbp, LastChangedBy, LastChangedAt)
                    VALUES (@convoyId, CAST(@vin AS varchar(32)), @equipmentItemId, @quantity, @costGbp, @changedBy, SYSUTCDATETIME())
                    """,
                    attribution.With(new
                    {
                        convoyId,
                        vin,
                        equipmentItemId = line.EquipmentItemId,
                        quantity = line.Quantity,
                        costGbp = line.CostGbp,
                    }),
                    transaction,
                    cancellationToken: cancellationToken));
            }
        }
        catch (SqlException exception) when (exception.Number == SqlErrors.ForeignKeyViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ReplaceEquipmentResult.UnknownItem;
        }

        await transaction.CommitAsync(cancellationToken);
        return ReplaceEquipmentResult.Replaced;
    }
}
