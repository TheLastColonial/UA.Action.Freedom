using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Data.Convoys;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.Convoys.ConvoyFixtures;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Convoys;

/// <summary>A convoy's budget lines and the costs entered against them, against a real database (O12, P3).</summary>
[Trait("Category", "Integration")]
public class ConvoyBudgetRepositoryTests
{
    private const string BudgetProbe =
        """
        SELECT COUNT(1) FROM dbo.Convoy;
        SELECT COUNT(1) FROM dbo.ConvoyBudgetLine;
        SELECT COUNT(1) FROM dbo.ConvoyCost;
        """;

    private static async Task<(ConvoyRepository Convoys, ConvoyBudgetRepository Budget)> ConnectOrSkipAsync(
        CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync(BudgetProbe, cancellationToken);
        return (new ConvoyRepository(ConnectionFactory(), Unattributed), new ConvoyBudgetRepository(ConnectionFactory(), Unattributed));
    }

    [Fact]
    public async Task Replacing_the_budget_leaves_exactly_the_lines_given()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, budget) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            await budget.ReplaceLinesAsync(
                id, [new BudgetLine(CostType.Fuel, 1_000m), new BudgetLine(CostType.Ferry, 600.50m)], cancellationToken);
            await budget.ReplaceLinesAsync(id, [new BudgetLine(CostType.Hotel, 300m)], cancellationToken);

            var lines = await budget.ListLinesAsync(id, cancellationToken);

            lines.Should().ContainSingle().Which.Should().Match<BudgetLineReadModel>(
                line => line.Type == CostType.Hotel && line.PlannedGbp == 300m);
        }
        finally
        {
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task A_line_keeps_pence_and_names_who_set_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SkipUnlessReachableAsync(BudgetProbe, cancellationToken);
        var person = await AddVolunteerAsync("Dora", "Dispatcher", isDriver: false);
        var convoys = new ConvoyRepository(ConnectionFactory(), Unattributed);
        var budget = new ConvoyBudgetRepository(ConnectionFactory(), AttributedTo(person));
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            await budget.ReplaceLinesAsync(id, [new BudgetLine(CostType.Fuel, 1_234.56m)], cancellationToken);

            var line = (await budget.ListLinesAsync(id, cancellationToken)).Single();

            line.PlannedGbp.Should().Be(1_234.56m);
            line.LastChangedByName.Should().Be("Dora Dispatcher");
            line.LastChangedAt.Should().NotBeNull();
        }
        finally
        {
            await RemoveConvoyAsync(id);
            await RemovePeopleAsync(person);
        }
    }

    [Fact]
    public async Task A_cost_is_added_listed_and_deleted_only_through_its_own_convoy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, budget) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var other = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            var costId = await budget.AddCostAsync(
                new ConvoyCostRecord(id, CostType.Fuel, 412.30m, "WVWZZZ1JZXW000001", "Diesel, Calais"), cancellationToken);

            var costs = await budget.ListCostsAsync(id, cancellationToken);

            costs.Should().ContainSingle().Which.Should().Match<ConvoyCostReadModel>(
                cost => cost.Id == costId && cost.Type == CostType.Fuel && cost.AmountGbp == 412.30m
                    && cost.Vin == "WVWZZZ1JZXW000001" && cost.Note == "Diesel, Calais");
            (await budget.DeleteCostAsync(other, costId, cancellationToken)).Should().BeFalse();
            (await budget.DeleteCostAsync(id, costId, cancellationToken)).Should().BeTrue();
            (await budget.ListCostsAsync(id, cancellationToken)).Should().BeEmpty();
        }
        finally
        {
            await RemoveConvoyAsync(id);
            await RemoveConvoyAsync(other);
        }
    }

    [Theory]
    [InlineData(CostType.Ferry)]
    [InlineData(CostType.Hotel)]
    [InlineData(CostType.Insurance)]
    public async Task The_database_refuses_to_store_a_cost_that_belongs_on_a_booking(CostType type)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, budget) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        try
        {
            var add = () => budget.AddCostAsync(new ConvoyCostRecord(id, type, 10m, null, null), cancellationToken);

            await add.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
        }
        finally
        {
            await RemoveConvoyAsync(id);
        }
    }

    [Fact]
    public async Task Deleting_a_convoy_takes_its_budget_and_costs_with_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, budget) = await ConnectOrSkipAsync(cancellationToken);
        var id = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);

        await budget.ReplaceLinesAsync(id, [new BudgetLine(CostType.Fuel, 100m)], cancellationToken);
        await budget.AddCostAsync(new ConvoyCostRecord(id, CostType.Other, 5m, null, null), cancellationToken);

        await RemoveConvoyAsync(id);

        (await ScalarAsync("SELECT COUNT(1) FROM dbo.ConvoyBudgetLine WHERE ConvoyId = @id", ("@id", id))).Should().Be(0);
        (await ScalarAsync("SELECT COUNT(1) FROM dbo.ConvoyCost WHERE ConvoyId = @id", ("@id", id))).Should().Be(0);
    }
}
