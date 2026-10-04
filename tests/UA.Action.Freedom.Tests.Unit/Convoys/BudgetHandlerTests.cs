using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// A convoy's budget: lines per cost type, costs entered against them, and the summary that sets the two beside the
/// costs already held on bookings (O12, P3). None of it blocks departure (O37).
/// </summary>
public class BudgetHandlerTests
{
    private const string Vin = "WVWZZZ1JZXW000001";

    private sealed record World(
        IConvoyRepository Convoys,
        IConvoyVehicleRepository TruckList,
        IConvoyBudgetRepository Budget,
        IVehicleEquipmentRepository Equipment);

    private static World AWorld(bool convoyExists = true)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns(convoyExists ? ConvoyTestData.AReadModel() : null);
        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.ListAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns([ConvoyTestData.AVehicle(Vin)]);
        truckList.GetAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>())
            .Returns(ConvoyTestData.AVehicle(Vin));
        var budget = Substitute.For<IConvoyBudgetRepository>();
        var equipment = Substitute.For<IVehicleEquipmentRepository>();
        equipment.ListForConvoyAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
        return new World(convoys, truckList, budget, equipment);
    }

    private static AddCostCommand AFuelCost(decimal amount = 100m, string? vin = Vin) =>
        new(ConvoyTestData.Id, CostType.Fuel, amount, vin, "Diesel, Calais");

    [Fact]
    public async Task Setting_the_budget_replaces_its_lines()
    {
        var world = AWorld();
        BudgetLine[] lines = [new(CostType.Fuel, 1_000m), new(CostType.Ferry, 600m)];

        var outcome = await new SetBudgetHandler(world.Convoys, world.Budget).HandleAsync(
            new SetBudgetCommand(ConvoyTestData.Id, lines), TestContext.Current.CancellationToken);

        outcome.Should().Be(SetBudgetOutcome.Set);
        await world.Budget.Received().ReplaceLinesAsync(ConvoyTestData.Id, lines, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Setting_the_budget_of_an_unknown_convoy_writes_nothing()
    {
        var world = AWorld(convoyExists: false);

        var outcome = await new SetBudgetHandler(world.Convoys, world.Budget).HandleAsync(
            new SetBudgetCommand(ConvoyTestData.Id, [new BudgetLine(CostType.Fuel, 1m)]),
            TestContext.Current.CancellationToken);

        outcome.Should().Be(SetBudgetOutcome.ConvoyNotFound);
        await world.Budget.DidNotReceiveWithAnyArgs().ReplaceLinesAsync(default, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_fuel_cost_is_recorded_against_a_vehicle_on_the_convoy_and_its_id_returned()
    {
        var world = AWorld();
        world.Budget.AddCostAsync(Arg.Any<ConvoyCostRecord>(), Arg.Any<CancellationToken>()).Returns(7);

        var result = await new AddCostHandler(world.Convoys, world.TruckList, world.Budget).HandleAsync(
            AFuelCost(), TestContext.Current.CancellationToken);

        result.Should().Be(new AddCostResult(AddCostOutcome.Created, 7));
    }

    [Theory]
    [InlineData(CostType.Ferry)]
    [InlineData(CostType.Hotel)]
    [InlineData(CostType.Insurance)]
    public async Task A_cost_held_on_a_booking_cannot_be_entered_a_second_time(CostType type)
    {
        var world = AWorld();

        var result = await new AddCostHandler(world.Convoys, world.TruckList, world.Budget).HandleAsync(
            AFuelCost() with { Type = type }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(AddCostOutcome.HeldOnBooking);
        await world.Budget.DidNotReceiveWithAnyArgs().AddCostAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_cost_against_a_vehicle_not_on_the_convoy_is_refused()
    {
        var world = AWorld();
        world.TruckList.GetAsync(ConvoyTestData.Id, "OTHER", Arg.Any<CancellationToken>())
            .Returns((ConvoyVehicleReadModel?)null);

        var result = await new AddCostHandler(world.Convoys, world.TruckList, world.Budget).HandleAsync(
            AFuelCost(vin: "OTHER"), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(AddCostOutcome.VehicleNotOnConvoy);
        await world.Budget.DidNotReceiveWithAnyArgs().AddCostAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_cost_against_an_unknown_convoy_is_refused()
    {
        var world = AWorld(convoyExists: false);

        var result = await new AddCostHandler(world.Convoys, world.TruckList, world.Budget).HandleAsync(
            AFuelCost(), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(AddCostOutcome.ConvoyNotFound);
    }

    [Fact]
    public async Task A_cost_need_not_name_a_vehicle()
    {
        var world = AWorld();
        world.Budget.AddCostAsync(Arg.Any<ConvoyCostRecord>(), Arg.Any<CancellationToken>()).Returns(3);

        var result = await new AddCostHandler(world.Convoys, world.TruckList, world.Budget).HandleAsync(
            AFuelCost(vin: null) with { Type = CostType.Other }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(AddCostOutcome.Created);
        await world.TruckList.DidNotReceiveWithAnyArgs().GetAsync(default, default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true, DeleteCostOutcome.Deleted)]
    [InlineData(false, DeleteCostOutcome.NotFound)]
    public async Task Deleting_a_cost_reports_what_the_write_found(bool deleted, DeleteCostOutcome expected)
    {
        var world = AWorld();
        world.Budget.DeleteCostAsync(ConvoyTestData.Id, 7, Arg.Any<CancellationToken>()).Returns(deleted);

        var outcome = await new DeleteCostHandler(world.Budget).HandleAsync(
            new DeleteCostCommand(ConvoyTestData.Id, 7), TestContext.Current.CancellationToken);

        outcome.Should().Be(expected);
    }

    [Fact]
    public async Task The_summary_shows_a_ferry_cost_from_its_booking_without_it_being_entered()
    {
        var world = AWorld();
        world.Budget.ListLinesAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns([new BudgetLineReadModel(CostType.Ferry, 500m)]);
        world.Budget.ListCostsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
        world.TruckList.GetFerryBookingAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>())
            .Returns(new FerryBookingReadModel(
                ConvoyTestData.Id, Vin, "P&O", "POF-1", ConvoyTestData.Start, null, 310m));

        var summary = await new GetBudgetSummaryHandler(world.Convoys, world.TruckList, world.Budget, world.Equipment).HandleAsync(
            new GetBudgetSummaryQuery(ConvoyTestData.Id), TestContext.Current.CancellationToken);

        var ferry = summary!.Lines.Single(line => line.Type == CostType.Ferry);
        ferry.ActualGbp.Should().Be(310m);
        ferry.PlannedGbp.Should().Be(500m);
        ferry.OverBudget.Should().BeFalse();
    }

    [Fact]
    public async Task The_summary_shows_insurance_from_the_policy_and_fuel_over_its_line()
    {
        var world = AWorld();
        world.Budget.ListLinesAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns([new BudgetLineReadModel(CostType.Fuel, 1_000m)]);
        world.Budget.ListCostsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(
        [
            new ConvoyCostReadModel(1, ConvoyTestData.Id, CostType.Fuel, 600m, Vin, null),
            new ConvoyCostReadModel(2, ConvoyTestData.Id, CostType.Fuel, 500m, null, null),
        ]);
        world.TruckList.GetInsuranceAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>())
            .Returns(new VehicleInsuranceReadModel(
                ConvoyTestData.Id, Vin, "Acme", "P-1", ConvoyTestData.Start, ConvoyTestData.ExpectedEnd,
                180m, Guid.NewGuid(), ConvoyTestData.Start, null));

        var summary = await new GetBudgetSummaryHandler(world.Convoys, world.TruckList, world.Budget, world.Equipment).HandleAsync(
            new GetBudgetSummaryQuery(ConvoyTestData.Id), TestContext.Current.CancellationToken);

        summary!.BudgetSet.Should().BeTrue();
        summary.AnyOverBudget.Should().BeTrue();
        summary.Lines.Single(line => line.Type == CostType.Fuel).OverBudget.Should().BeTrue();
        summary.Lines.Single(line => line.Type == CostType.Insurance).ActualGbp.Should().Be(180m);
        summary.ActualTotalGbp.Should().Be(1_280m);
    }

    [Fact]
    public async Task The_summary_of_an_unknown_convoy_is_null()
    {
        var world = AWorld(convoyExists: false);

        var summary = await new GetBudgetSummaryHandler(world.Convoys, world.TruckList, world.Budget, world.Equipment).HandleAsync(
            new GetBudgetSummaryQuery(ConvoyTestData.Id), TestContext.Current.CancellationToken);

        summary.Should().BeNull();
    }
}
