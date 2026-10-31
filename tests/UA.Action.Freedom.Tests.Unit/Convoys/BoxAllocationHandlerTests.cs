using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// Allocating a box to a truck-list entry: the checks that come before the write.
/// </summary>
public class BoxAllocationHandlerTests
{
    private const int ConvoyId = 42;
    private const string Vin = "WVWZZZ1JZXW000001";
    private const int BoxId = 7;

    private static readonly DateTime Now = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    private static ConvoyVehicleReadModel AnEntry(DateTime? withdrawnAt = null) =>
        new(Vin, "AB12CDE", 1_800, 1, 0, withdrawnAt);

    private static IConvoyVehicleRepository Repository(ConvoyVehicleReadModel? entry)
    {
        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.GetAsync(ConvoyId, Vin, Arg.Any<CancellationToken>()).Returns(entry);
        truckList.AllocateBoxAsync(ConvoyId, Vin, BoxId, Arg.Any<CancellationToken>()).Returns(AllocateBoxResult.Allocated);
        truckList.RemoveBoxAsync(ConvoyId, Vin, BoxId, Arg.Any<CancellationToken>()).Returns(true);

        return truckList;
    }

    private static Task<AllocateBoxOutcome> Allocate(IConvoyVehicleRepository truckList) =>
        new AllocateBoxHandler(truckList)
            .HandleAsync(new AllocateBoxCommand(ConvoyId, Vin, BoxId), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Allocates_a_box_to_a_vehicle_on_the_convoy()
    {
        var truckList = Repository(AnEntry());

        (await Allocate(truckList)).Should().Be(AllocateBoxOutcome.Allocated);
    }

    [Theory]
    [InlineData(AllocateBoxResult.Moved, AllocateBoxOutcome.Moved)]
    [InlineData(AllocateBoxResult.AlreadyAllocated, AllocateBoxOutcome.AlreadyAllocated)]
    [InlineData(AllocateBoxResult.BoxNotFound, AllocateBoxOutcome.BoxNotFound)]
    [InlineData(AllocateBoxResult.BoxVoided, AllocateBoxOutcome.BoxVoided)]
    [InlineData(AllocateBoxResult.VehicleWithdrawn, AllocateBoxOutcome.VehicleWithdrawn)]
    [InlineData(AllocateBoxResult.VehicleNotOnConvoy, AllocateBoxOutcome.VehicleNotOnConvoy)]
    public async Task Reports_what_the_write_found(AllocateBoxResult found, AllocateBoxOutcome expected)
    {
        var truckList = Repository(AnEntry());
        truckList.AllocateBoxAsync(ConvoyId, Vin, BoxId, Arg.Any<CancellationToken>()).Returns(found);

        (await Allocate(truckList)).Should().Be(expected);
    }

    [Fact]
    public async Task A_vehicle_that_is_not_on_the_convoy_takes_no_cargo()
    {
        var truckList = Repository(entry: null);

        (await Allocate(truckList)).Should().Be(AllocateBoxOutcome.VehicleNotOnConvoy);
        await truckList.DidNotReceive().AllocateBoxAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_withdrawn_vehicle_takes_no_cargo()
    {
        var truckList = Repository(AnEntry(withdrawnAt: Now));

        (await Allocate(truckList)).Should().Be(AllocateBoxOutcome.VehicleWithdrawn);
        await truckList.DidNotReceive().AllocateBoxAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removes_a_box_from_a_vehicle()
    {
        var truckList = Repository(AnEntry());

        var outcome = await new RemoveBoxAllocationHandler(truckList)
            .HandleAsync(new RemoveBoxAllocationCommand(ConvoyId, Vin, BoxId), TestContext.Current.CancellationToken);

        outcome.Should().Be(RemoveBoxAllocationOutcome.Removed);
    }

    [Fact]
    public async Task Removing_a_box_that_is_not_on_the_vehicle_says_so()
    {
        var truckList = Repository(AnEntry());
        truckList.RemoveBoxAsync(ConvoyId, Vin, BoxId, Arg.Any<CancellationToken>()).Returns(false);

        var outcome = await new RemoveBoxAllocationHandler(truckList)
            .HandleAsync(new RemoveBoxAllocationCommand(ConvoyId, Vin, BoxId), TestContext.Current.CancellationToken);

        outcome.Should().Be(RemoveBoxAllocationOutcome.NotAllocated);
    }
}
