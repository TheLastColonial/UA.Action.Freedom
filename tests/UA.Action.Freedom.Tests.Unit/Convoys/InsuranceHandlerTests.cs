using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// Recording a vehicle's insurance for a convoy. Whether the vehicle is travelling with it is
/// settled by the write; the handler only tells "no such convoy" and "already arrived" apart.
/// </summary>
public class InsuranceHandlerTests
{
    private const string Vin = "WVWZZZ1JZXW000001";

    private static VehicleInsuranceRecord APolicy() => new(
        ConvoyTestData.Id, Vin, "Ukraine Aid Mutual", "POL-1",
        new DateTime(2026, 8, 25), new DateTime(2026, 9, 30), 412.50m, "operator-sub");

    private static (IConvoyRepository Convoys, IConvoyVehicleRepository TruckList) Repositories(ConvoyReadModel? convoy)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(convoy);
        return (convoys, Substitute.For<IConvoyVehicleRepository>());
    }

    [Theory]
    [InlineData(true, RecordInsuranceOutcome.Recorded)]
    [InlineData(false, RecordInsuranceOutcome.VehicleNotOnConvoy)]
    public async Task Reports_what_the_write_found(bool written, RecordInsuranceOutcome expected)
    {
        var (convoys, truckList) = Repositories(ConvoyTestData.AReadModel());
        truckList.RecordInsuranceAsync(APolicy(), Arg.Any<CancellationToken>()).Returns(written);

        var outcome = await new RecordInsuranceHandler(convoys, truckList).HandleAsync(
            new RecordInsuranceCommand(APolicy()), TestContext.Current.CancellationToken);

        outcome.Should().Be(expected);
    }

    [Fact]
    public async Task Reports_an_unknown_convoy_without_writing()
    {
        var (convoys, truckList) = Repositories(convoy: null);

        var outcome = await new RecordInsuranceHandler(convoys, truckList).HandleAsync(
            new RecordInsuranceCommand(APolicy()), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordInsuranceOutcome.ConvoyNotFound);
        await truckList.DidNotReceive().RecordInsuranceAsync(
            Arg.Any<VehicleInsuranceRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_to_insure_a_convoy_that_has_arrived()
    {
        var (convoys, truckList) = Repositories(ConvoyTestData.AnArrivedConvoy());

        var outcome = await new RecordInsuranceHandler(convoys, truckList).HandleAsync(
            new RecordInsuranceCommand(APolicy()), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordInsuranceOutcome.ConvoyArrived);
        await truckList.DidNotReceive().RecordInsuranceAsync(
            Arg.Any<VehicleInsuranceRecord>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, RemoveInsuranceOutcome.Removed)]
    [InlineData(false, RemoveInsuranceOutcome.NotFound)]
    public async Task Removes_insurance(bool removed, RemoveInsuranceOutcome expected)
    {
        var (convoys, truckList) = Repositories(ConvoyTestData.AReadModel());
        truckList.RemoveInsuranceAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns(removed);

        var outcome = await new RemoveInsuranceHandler(convoys, truckList).HandleAsync(
            new RemoveInsuranceCommand(ConvoyTestData.Id, Vin), TestContext.Current.CancellationToken);

        outcome.Should().Be(expected);
    }

    [Fact]
    public async Task Reads_a_policy_through_the_truck_list()
    {
        var policy = new VehicleInsuranceReadModel(
            ConvoyTestData.Id, Vin, "Ukraine Aid Mutual", "POL-1",
            new DateTime(2026, 8, 25), new DateTime(2026, 9, 30), 412.50m, "operator-sub",
            new DateTime(2026, 8, 20), VoidedAt: null);

        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.GetInsuranceAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns(policy);

        var found = await new GetInsuranceHandler(truckList).HandleAsync(
            new GetInsuranceQuery(ConvoyTestData.Id, Vin), TestContext.Current.CancellationToken);

        found.Should().Be(policy);
    }
}
