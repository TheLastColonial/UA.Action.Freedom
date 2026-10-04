using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// Equipment the charity buys for a vehicle (O13): a step of creating a convoy, with no donor, counted against the
/// Other budget line and kept out of the value delivered.
/// </summary>
public class VehicleEquipmentHandlerTests
{
    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly VehicleEquipmentLine[] Triangles = [new(1, 2, null)];

    private static IConvoyRepository Convoys(ConvoyReadModel? convoy)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(convoy);
        return convoys;
    }

    [Theory]
    [InlineData(ReplaceEquipmentResult.Replaced, SetVehicleEquipmentOutcome.Set)]
    [InlineData(ReplaceEquipmentResult.VehicleNotOnConvoy, SetVehicleEquipmentOutcome.VehicleNotOnConvoy)]
    [InlineData(ReplaceEquipmentResult.UnknownItem, SetVehicleEquipmentOutcome.UnknownItem)]
    public async Task Reports_what_the_write_found(ReplaceEquipmentResult result, SetVehicleEquipmentOutcome expected)
    {
        var equipment = Substitute.For<IVehicleEquipmentRepository>();
        equipment.ReplaceForVehicleAsync(ConvoyTestData.Id, Vin, Triangles, Arg.Any<CancellationToken>()).Returns(result);

        var outcome = await new SetVehicleEquipmentHandler(Convoys(ConvoyTestData.AReadModel()), equipment).HandleAsync(
            new SetVehicleEquipmentCommand(ConvoyTestData.Id, Vin, Triangles), TestContext.Current.CancellationToken);

        outcome.Should().Be(expected);
    }

    [Fact]
    public async Task Writes_nothing_for_an_unknown_or_arrived_convoy()
    {
        var equipment = Substitute.For<IVehicleEquipmentRepository>();

        var unknown = await new SetVehicleEquipmentHandler(Convoys(null), equipment).HandleAsync(
            new SetVehicleEquipmentCommand(ConvoyTestData.Id, Vin, Triangles), TestContext.Current.CancellationToken);
        var arrived = await new SetVehicleEquipmentHandler(Convoys(ConvoyTestData.AnArrivedConvoy()), equipment).HandleAsync(
            new SetVehicleEquipmentCommand(ConvoyTestData.Id, Vin, Triangles), TestContext.Current.CancellationToken);

        unknown.Should().Be(SetVehicleEquipmentOutcome.ConvoyNotFound);
        arrived.Should().Be(SetVehicleEquipmentOutcome.ConvoyArrived);
        await equipment.DidNotReceiveWithAnyArgs().ReplaceForVehicleAsync(
            default, default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_vehicle_not_on_the_convoy_has_no_equipment_to_read()
    {
        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.GetAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns((ConvoyVehicleReadModel?)null);

        var lines = await new GetVehicleEquipmentHandler(truckList, Substitute.For<IVehicleEquipmentRepository>()).HandleAsync(
            new GetVehicleEquipmentQuery(ConvoyTestData.Id, Vin), TestContext.Current.CancellationToken);

        lines.Should().BeNull();
    }

    [Fact]
    public async Task A_catalogue_name_already_taken_is_reported_not_added()
    {
        var equipment = Substitute.For<IVehicleEquipmentRepository>();
        equipment.AddItemAsync("Warning triangle", 6m, Arg.Any<CancellationToken>()).Returns((int?)null);

        var result = await new AddEquipmentItemHandler(equipment).HandleAsync(
            new AddEquipmentItemCommand("Warning triangle", 6m), TestContext.Current.CancellationToken);

        result.Should().Be(new AddEquipmentItemResult(AddEquipmentItemOutcome.NameTaken));
    }

    [Fact]
    public async Task Equipment_counts_under_Other_in_the_summary_and_is_shown_separately()
    {
        var convoys = Convoys(ConvoyTestData.AReadModel());
        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.ListAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([ConvoyTestData.AVehicle(Vin)]);
        var budget = Substitute.For<IConvoyBudgetRepository>();
        budget.ListLinesAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns([new BudgetLineReadModel(CostType.Other, 20m)]);
        budget.ListCostsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns([]);
        var equipment = Substitute.For<IVehicleEquipmentRepository>();
        equipment.ListForConvoyAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(
        [
            new VehicleEquipmentReadModel(Vin, 1, "Warning triangle", 2, 6.50m, null),
            new VehicleEquipmentReadModel(Vin, 2, "Tow strap", 1, null, 12m),
        ]);

        var summary = await new GetBudgetSummaryHandler(convoys, truckList, budget, equipment).HandleAsync(
            new GetBudgetSummaryQuery(ConvoyTestData.Id), TestContext.Current.CancellationToken);

        summary!.EquipmentGbp.Should().Be(25m);
        var other = summary.Lines.Single(line => line.Type == CostType.Other);
        other.ActualGbp.Should().Be(25m);
        other.OverBudget.Should().BeTrue();
    }
}
