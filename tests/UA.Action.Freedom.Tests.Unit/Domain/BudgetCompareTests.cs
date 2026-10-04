using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>
/// A convoy's budget has a line per cost type and actual costs are compared with each (O12, P3). Going over a
/// line is advisory (O37), so the comparison only reports it.
/// </summary>
public class BudgetCompareTests
{
    private static ActualCost AnEntered(CostType type, decimal amount) => new(type, amount);

    private static ActualCost ADerived(CostType type, decimal amount) => new(type, amount, Derived: true);

    private static BudgetComparison For(IReadOnlyList<BudgetComparison> comparison, CostType type) =>
        comparison.Single(line => line.Type == type);

    [Fact]
    public void Every_cost_type_is_reported_even_when_nothing_is_budgeted_or_spent()
    {
        var comparison = Budget.Compare([], []);

        comparison.Select(line => line.Type).Should().Equal(
            CostType.Fuel, CostType.Ferry, CostType.Hotel, CostType.Insurance, CostType.Other);
        comparison.Should().OnlyContain(line => line.PlannedGbp == null && line.ActualGbp == 0m && !line.OverBudget);
    }

    [Fact]
    public void Actual_costs_of_one_type_are_added_together()
    {
        var comparison = Budget.Compare(
            [new BudgetLine(CostType.Fuel, 1_000m)],
            [AnEntered(CostType.Fuel, 400m), AnEntered(CostType.Fuel, 250.50m), AnEntered(CostType.Other, 20m)]);

        var fuel = For(comparison, CostType.Fuel);
        fuel.PlannedGbp.Should().Be(1_000m);
        fuel.ActualGbp.Should().Be(650.50m);
        fuel.OverBudget.Should().BeFalse();
    }

    [Fact]
    public void A_line_is_over_budget_only_once_the_actual_exceeds_the_plan()
    {
        var atTheLimit = Budget.Compare([new BudgetLine(CostType.Fuel, 1_000m)], [AnEntered(CostType.Fuel, 1_000m)]);
        var overTheLimit = Budget.Compare([new BudgetLine(CostType.Fuel, 1_000m)], [AnEntered(CostType.Fuel, 1_100m)]);

        For(atTheLimit, CostType.Fuel).OverBudget.Should().BeFalse();
        For(overTheLimit, CostType.Fuel).OverBudget.Should().BeTrue();
    }

    [Fact]
    public void A_derived_booking_cost_counts_against_its_line_like_an_entered_one()
    {
        var comparison = Budget.Compare(
            [new BudgetLine(CostType.Ferry, 500m), new BudgetLine(CostType.Insurance, 200m)],
            [ADerived(CostType.Ferry, 310m), ADerived(CostType.Ferry, 310m), ADerived(CostType.Insurance, 150m)]);

        var ferry = For(comparison, CostType.Ferry);
        ferry.ActualGbp.Should().Be(620m);
        ferry.OverBudget.Should().BeTrue();
        For(comparison, CostType.Insurance).OverBudget.Should().BeFalse();
    }

    [Fact]
    public void Spending_on_a_type_with_no_line_is_shown_but_is_not_over_budget()
    {
        var hotel = For(Budget.Compare([], [AnEntered(CostType.Hotel, 90m)]), CostType.Hotel);

        hotel.PlannedGbp.Should().BeNull();
        hotel.ActualGbp.Should().Be(90m);
        hotel.OverBudget.Should().BeFalse();
    }

    [Fact]
    public void A_line_budgeted_at_nothing_is_over_budget_as_soon_as_anything_is_spent()
    {
        var other = For(Budget.Compare([new BudgetLine(CostType.Other, 0m)], [AnEntered(CostType.Other, 5m)]), CostType.Other);

        other.OverBudget.Should().BeTrue();
    }

    [Fact]
    public void A_budget_is_set_once_any_line_exists()
    {
        Budget.IsSet([]).Should().BeFalse();
        Budget.IsSet([new BudgetLine(CostType.Fuel, 0m)]).Should().BeTrue();
    }
}
