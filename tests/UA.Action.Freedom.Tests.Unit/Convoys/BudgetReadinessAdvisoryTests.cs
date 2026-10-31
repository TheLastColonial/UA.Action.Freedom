using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// O37, P17: a convoy with no budget, or a line over its budget, is advice on the readiness read. It never makes the
/// convoy not ready, because a budget is not required to depart.
/// </summary>
public class BudgetReadinessAdvisoryTests
{
    private static BudgetSummaryReadModel ASummary(bool set, params BudgetSummaryLine[] lines) => new(
        set, lines, 0m, 0m, 0m, lines.Any(line => line.OverBudget));

    [Fact]
    public void A_convoy_with_no_budget_is_advised_to_set_one()
    {
        BudgetAdvisories.For(ASummary(set: false)).Should().Equal("No budget set");
    }

    [Fact]
    public void Each_line_over_its_budget_is_named()
    {
        var advisories = BudgetAdvisories.For(ASummary(
            set: true,
            new BudgetSummaryLine(CostType.Fuel, 1_000m, 1_100m, OverBudget: true),
            new BudgetSummaryLine(CostType.Ferry, 600m, 310m, OverBudget: false),
            new BudgetSummaryLine(CostType.Other, 20m, 25m, OverBudget: true)));

        advisories.Should().Equal("Fuel is over budget", "Other is over budget");
    }

    [Fact]
    public void A_budget_within_its_lines_has_nothing_to_say()
    {
        BudgetAdvisories.For(ASummary(
            set: true, new BudgetSummaryLine(CostType.Fuel, 1_000m, 400m, OverBudget: false))).Should().BeEmpty();
    }

    [Fact]
    public async Task The_readiness_carries_the_advice_and_is_not_blocked_by_it()
    {
        var readiness = await new ReadinessWorld().ReadAsync();

        readiness!.Advisories.Should().Equal("No budget set");
        readiness.Requirements.Single(requirement => requirement.Code == "advisory.budget")
            .Severity.Should().Be(RequirementSeverity.Advisory);
    }
}
