using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Convoys;

public sealed record BudgetLineReadModel(
    CostType Type,
    decimal PlannedGbp,
    string? LastChangedByName = null,
    DateTime? LastChangedAt = null);

/// <summary>A cost somebody entered. Who entered it is the stamp: costs are added and deleted, never edited.</summary>
public sealed record ConvoyCostRecord(int ConvoyId, CostType Type, decimal AmountGbp, string? Vin, string? Note);

public sealed record ConvoyCostReadModel(
    int Id,
    int ConvoyId,
    CostType Type,
    decimal AmountGbp,
    string? Vin,
    string? Note,
    string? LastChangedByName = null,
    DateTime? LastChangedAt = null);

/// <summary>
/// Persistence port for a convoy's budget lines and the costs entered against them. Ferry, hotel and insurance
/// amounts are not stored here: they stay on their booking or policy and are read from it.
/// </summary>
public interface IConvoyBudgetRepository
{
    Task<IReadOnlyList<BudgetLineReadModel>> ListLinesAsync(int convoyId, CancellationToken cancellationToken);

    /// <summary>Replaces every line in one transaction: a type left out has no line afterwards.</summary>
    Task ReplaceLinesAsync(int convoyId, IReadOnlyList<BudgetLine> lines, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConvoyCostReadModel>> ListCostsAsync(int convoyId, CancellationToken cancellationToken);

    /// <summary>Records a cost and returns its new identifier.</summary>
    Task<int> AddCostAsync(ConvoyCostRecord cost, CancellationToken cancellationToken);

    /// <summary>Deletes a cost of this convoy. Returns false when there is no such cost on it.</summary>
    Task<bool> DeleteCostAsync(int convoyId, int costId, CancellationToken cancellationToken);
}
