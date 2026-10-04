using Dapper;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Data.Convoys;

/// <summary>
/// Dapper-backed <see cref="IConvoyBudgetRepository"/> over <c>dbo.ConvoyBudgetLine</c> and <c>dbo.ConvoyCost</c>.
/// </summary>
public sealed class ConvoyBudgetRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution)
    : IConvoyBudgetRepository
{
    public async Task<IReadOnlyList<BudgetLineReadModel>> ListLinesAsync(int convoyId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<BudgetLineReadModel>(new CommandDefinition(
            $"""
             SELECT l.CostType AS [Type], l.PlannedGbp, {ChangeStamp.ReadColumns("l")}
             FROM dbo.ConvoyBudgetLine AS l {ChangeStamp.ReadJoin("l")}
             WHERE l.ConvoyId = @convoyId
             ORDER BY l.CostType
             """,
            new { convoyId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task ReplaceLinesAsync(
        int convoyId, IReadOnlyList<BudgetLine> lines, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.ConvoyBudgetLine WHERE ConvoyId = @convoyId",
            new { convoyId },
            transaction,
            cancellationToken: cancellationToken));

        foreach (var line in lines)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.ConvoyBudgetLine (ConvoyId, CostType, PlannedGbp, LastChangedBy, LastChangedAt)
                VALUES (@convoyId, @costType, @plannedGbp, @changedBy, SYSUTCDATETIME())
                """,
                attribution.With(new { convoyId, costType = (int)line.Type, plannedGbp = line.PlannedGbp }),
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConvoyCostReadModel>> ListCostsAsync(int convoyId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<ConvoyCostReadModel>(new CommandDefinition(
            $"""
             SELECT c.Id, c.ConvoyId, c.CostType AS [Type], c.AmountGbp, c.Vin, c.Note, {ChangeStamp.ReadColumns("c")}
             FROM dbo.ConvoyCost AS c {ChangeStamp.ReadJoin("c")}
             WHERE c.ConvoyId = @convoyId
             ORDER BY c.Id
             """,
            new { convoyId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<int> AddCostAsync(ConvoyCostRecord cost, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleAsync<int>(new CommandDefinition(
            """
            INSERT INTO dbo.ConvoyCost (ConvoyId, CostType, Vin, AmountGbp, Note, LastChangedBy, LastChangedAt)
            OUTPUT INSERTED.Id
            VALUES (@convoyId, @costType, CAST(@vin AS varchar(32)), @amountGbp, @note, @changedBy, SYSUTCDATETIME())
            """,
            attribution.With(new
            {
                convoyId = cost.ConvoyId,
                costType = (int)cost.Type,
                vin = cost.Vin,
                amountGbp = cost.AmountGbp,
                note = cost.Note,
            }),
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteCostAsync(int convoyId, int costId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.ConvoyCost WHERE Id = @costId AND ConvoyId = @convoyId",
            new { convoyId, costId },
            cancellationToken: cancellationToken));

        return affected > 0;
    }
}
