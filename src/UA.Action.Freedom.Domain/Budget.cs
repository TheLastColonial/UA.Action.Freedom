namespace UA.Action.Freedom.Domain;

/// <summary>What a convoy spends money on (O12). Fuel and Other are entered; the rest are held with their booking.</summary>
public enum CostType
{
    Fuel,
    Ferry,
    Hotel,
    Insurance,
    Other
}

public sealed record BudgetLine(CostType Type, decimal PlannedGbp);

/// <summary>
/// Money spent against a line. <paramref name="Derived"/> says the amount was read from a booking or policy rather than
/// entered, so it is never entered twice.
/// </summary>
public sealed record ActualCost(CostType Type, decimal AmountGbp, string? Vin = null, string? Note = null, bool Derived = false);

/// <summary>One cost type's plan beside its actual. <see cref="PlannedGbp"/> is null when no line was budgeted.</summary>
public sealed record BudgetComparison(CostType Type, decimal? PlannedGbp, decimal ActualGbp, bool OverBudget);

public static class Budget
{
    public static bool IsSet(IReadOnlyCollection<BudgetLine> lines) => lines.Count > 0;

    public static IReadOnlyList<BudgetComparison> Compare(
        IReadOnlyCollection<BudgetLine> lines, IReadOnlyCollection<ActualCost> actuals) =>
        Enum.GetValues<CostType>()
            .Select(type =>
            {
                decimal? planned = lines.Where(line => line.Type == type).Select(line => (decimal?)line.PlannedGbp).FirstOrDefault();
                var actual = actuals.Where(cost => cost.Type == type).Sum(cost => cost.AmountGbp);
                return new BudgetComparison(type, planned, actual, planned is { } limit && actual > limit);
            })
            .ToList();
}
