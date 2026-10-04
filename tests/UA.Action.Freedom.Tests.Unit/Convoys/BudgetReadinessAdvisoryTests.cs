using AwesomeAssertions;
using NSubstitute;
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
    public async Task The_readiness_carries_the_advice_and_is_still_ready()
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(ConvoyTestData.AReadModel());
        convoys.GetRouteAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([ConvoyTestData.AStop(1)]);
        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.ListAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([ConvoyTestData.AVehicle("VIN-1")]);
        truckList.GetInsuranceAsync(ConvoyTestData.Id, "VIN-1", Arg.Any<CancellationToken>()).Returns(
            new VehicleInsuranceReadModel(
                ConvoyTestData.Id, "VIN-1", "Acme", "P-1", ConvoyTestData.Start.AddDays(-7), ConvoyTestData.Start.AddDays(30),
                null, Guid.Empty, ConvoyTestData.Start.AddDays(-10), null));
        var budget = Substitute.For<IConvoyBudgetRepository>();
        budget.ListLinesAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
        budget.ListCostsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
        var equipment = Substitute.For<IVehicleEquipmentRepository>();
        equipment.ListForConvoyAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);

        var readiness = await new GetConvoyReadinessHandler(
            convoys, truckList, new BudgetPosition(convoys, truckList, budget, equipment)).HandleAsync(
            new GetConvoyReadinessQuery(ConvoyTestData.Id), TestContext.Current.CancellationToken);

        readiness!.Ready.Should().BeTrue();
        readiness.Advisories.Should().Equal("No budget set");
    }
}
