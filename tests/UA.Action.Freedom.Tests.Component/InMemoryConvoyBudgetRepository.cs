using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// In-memory stand-in for <c>ConvoyBudgetRepository</c>. It mirrors the SQL: a budget is replaced as a whole with at
/// most one line per cost type, a cost is deleted only through the convoy it belongs to, and each row records who
/// last changed it. The CASCADE from <c>dbo.Convoy</c> is not mirrored, because this store does not own convoys; the
/// integration tests prove it.
/// </summary>
internal sealed class InMemoryConvoyBudgetRepository : IConvoyBudgetRepository, IRecordsWhoChanged
{
    private readonly ChangeLedger<string> changes = new();
    private readonly Dictionary<(int ConvoyId, CostType Type), decimal> lines = [];
    private readonly List<(ConvoyCostRecord Cost, int Id)> costs = [];
    private int nextCostId = 1;

    public void Attach(IChangeAttribution attribution, IPersonRepository people) => changes.Attach(attribution, people);

    private static string LineKey(int convoyId, CostType type) => $"line/{convoyId}/{type}";

    private static string CostKey(int costId) => $"cost/{costId}";

    public Task<IReadOnlyList<BudgetLineReadModel>> ListLinesAsync(int convoyId, CancellationToken cancellationToken)
    {
        IReadOnlyList<BudgetLineReadModel> result = lines
            .Where(line => line.Key.ConvoyId == convoyId)
            .OrderBy(line => line.Key.Type)
            .Select(line =>
            {
                var (name, at) = changes.Of(LineKey(convoyId, line.Key.Type));
                return new BudgetLineReadModel(line.Key.Type, line.Value, name, at);
            })
            .ToList();

        return Task.FromResult(result);
    }

    public Task ReplaceLinesAsync(int convoyId, IReadOnlyList<BudgetLine> replacement, CancellationToken cancellationToken)
    {
        foreach (var existing in lines.Keys.Where(key => key.ConvoyId == convoyId).ToList())
        {
            lines.Remove(existing);
            changes.Forget(LineKey(convoyId, existing.Type));
        }

        foreach (var line in replacement)
        {
            lines.Add((convoyId, line.Type), line.PlannedGbp);
            changes.Stamp(LineKey(convoyId, line.Type));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ConvoyCostReadModel>> ListCostsAsync(int convoyId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ConvoyCostReadModel> result = costs
            .Where(entry => entry.Cost.ConvoyId == convoyId)
            .Select(entry =>
            {
                var (name, at) = changes.Of(CostKey(entry.Id));
                return new ConvoyCostReadModel(
                    entry.Id, convoyId, entry.Cost.Type, entry.Cost.AmountGbp, entry.Cost.Vin, entry.Cost.Note, name, at);
            })
            .ToList();

        return Task.FromResult(result);
    }

    public Task<int> AddCostAsync(ConvoyCostRecord cost, CancellationToken cancellationToken)
    {
        var id = nextCostId++;
        costs.Add((cost, id));
        changes.Stamp(CostKey(id));
        return Task.FromResult(id);
    }

    public Task<bool> DeleteCostAsync(int convoyId, int costId, CancellationToken cancellationToken)
    {
        var removed = costs.RemoveAll(entry => entry.Id == costId && entry.Cost.ConvoyId == convoyId) > 0;
        if (removed)
        {
            changes.Forget(CostKey(costId));
        }

        return Task.FromResult(removed);
    }
}
