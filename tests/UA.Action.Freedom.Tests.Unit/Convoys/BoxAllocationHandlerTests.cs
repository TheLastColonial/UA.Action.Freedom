using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// Allocating a box to a truck-list entry: the checks that come before the write.
/// </summary>
public class BoxAllocationHandlerTests
{
    private const int ConvoyId = 42;
    private const string Vin = "WVWZZZ1JZXW000001";
    private const string OtherVin = "WVWZZZ1JZXW000002";
    private const int BoxId = 7;

    private static readonly DateTime Now = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    private static ConvoyVehicleReadModel AnEntry(DateTime? withdrawnAt = null) =>
        new(Vin, "AB12CDE", 1_800, 1, 0, withdrawnAt);

    private static ManifestReadModel AManifest(string vin, bool frozen) => new(
        "MAN-1", ConvoyId, vin, ManifestStatus.Preparing, null, frozen ? Now : null);

    private static (IConvoyVehicleRepository TruckList, IManifestRepository Manifests) Repositories(
        ConvoyVehicleReadModel? entry, ManifestReadModel? manifestOnTarget = null)
    {
        var truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.GetAsync(ConvoyId, Vin, Arg.Any<CancellationToken>()).Returns(entry);
        truckList.AllocateBoxAsync(ConvoyId, Vin, BoxId, Arg.Any<CancellationToken>()).Returns(AllocateBoxResult.Allocated);
        truckList.RemoveBoxAsync(ConvoyId, Vin, BoxId, Arg.Any<CancellationToken>()).Returns(true);

        var manifests = Substitute.For<IManifestRepository>();
        manifests.GetForVehicleAsync(ConvoyId, Vin, Arg.Any<CancellationToken>()).Returns(manifestOnTarget);

        return (truckList, manifests);
    }

    private static Task<AllocateBoxOutcome> Allocate(IConvoyVehicleRepository truckList, IManifestRepository manifests) =>
        new AllocateBoxHandler(truckList, manifests)
            .HandleAsync(new AllocateBoxCommand(ConvoyId, Vin, BoxId), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Allocates_a_box_to_a_vehicle_on_the_convoy()
    {
        var (truckList, manifests) = Repositories(AnEntry());

        (await Allocate(truckList, manifests)).Should().Be(AllocateBoxOutcome.Allocated);
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
        var (truckList, manifests) = Repositories(AnEntry());
        truckList.AllocateBoxAsync(ConvoyId, Vin, BoxId, Arg.Any<CancellationToken>()).Returns(found);

        (await Allocate(truckList, manifests)).Should().Be(expected);
    }

    [Fact]
    public async Task A_vehicle_that_is_not_on_the_convoy_takes_no_cargo()
    {
        var (truckList, manifests) = Repositories(entry: null);

        (await Allocate(truckList, manifests)).Should().Be(AllocateBoxOutcome.VehicleNotOnConvoy);
        await truckList.DidNotReceive().AllocateBoxAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_withdrawn_vehicle_takes_no_cargo()
    {
        var (truckList, manifests) = Repositories(AnEntry(withdrawnAt: Now));

        (await Allocate(truckList, manifests)).Should().Be(AllocateBoxOutcome.VehicleWithdrawn);
        await truckList.DidNotReceive().AllocateBoxAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_vehicle_with_a_frozen_manifest_takes_no_cargo()
    {
        var (truckList, manifests) = Repositories(AnEntry(), AManifest(Vin, frozen: true));

        (await Allocate(truckList, manifests)).Should().Be(AllocateBoxOutcome.Frozen);
        await truckList.DidNotReceive().AllocateBoxAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_box_cannot_be_moved_off_a_vehicle_with_a_frozen_manifest()
    {
        var (truckList, manifests) = Repositories(AnEntry());
        truckList.GetBoxAllocationAsync(BoxId, Arg.Any<CancellationToken>())
            .Returns(new BoxAllocation(new ConvoyId(ConvoyId), OtherVin, BoxId, Now));
        manifests.GetForVehicleAsync(ConvoyId, OtherVin, Arg.Any<CancellationToken>())
            .Returns(AManifest(OtherVin, frozen: true));

        (await Allocate(truckList, manifests)).Should().Be(AllocateBoxOutcome.Frozen);
        await truckList.DidNotReceive().AllocateBoxAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_box_can_be_moved_off_a_vehicle_whose_manifest_is_not_frozen()
    {
        var (truckList, manifests) = Repositories(AnEntry());
        truckList.GetBoxAllocationAsync(BoxId, Arg.Any<CancellationToken>())
            .Returns(new BoxAllocation(new ConvoyId(ConvoyId), OtherVin, BoxId, Now));
        manifests.GetForVehicleAsync(ConvoyId, OtherVin, Arg.Any<CancellationToken>())
            .Returns(AManifest(OtherVin, frozen: false));
        truckList.AllocateBoxAsync(ConvoyId, Vin, BoxId, Arg.Any<CancellationToken>()).Returns(AllocateBoxResult.Moved);

        (await Allocate(truckList, manifests)).Should().Be(AllocateBoxOutcome.Moved);
    }

    [Fact]
    public async Task Removes_a_box_from_a_vehicle()
    {
        var (truckList, manifests) = Repositories(AnEntry());

        var outcome = await new RemoveBoxAllocationHandler(truckList, manifests)
            .HandleAsync(new RemoveBoxAllocationCommand(ConvoyId, Vin, BoxId), TestContext.Current.CancellationToken);

        outcome.Should().Be(RemoveBoxAllocationOutcome.Removed);
    }

    [Fact]
    public async Task Removing_a_box_that_is_not_on_the_vehicle_says_so()
    {
        var (truckList, manifests) = Repositories(AnEntry());
        truckList.RemoveBoxAsync(ConvoyId, Vin, BoxId, Arg.Any<CancellationToken>()).Returns(false);

        var outcome = await new RemoveBoxAllocationHandler(truckList, manifests)
            .HandleAsync(new RemoveBoxAllocationCommand(ConvoyId, Vin, BoxId), TestContext.Current.CancellationToken);

        outcome.Should().Be(RemoveBoxAllocationOutcome.NotAllocated);
    }

    [Fact]
    public async Task A_vehicle_with_a_frozen_manifest_keeps_its_cargo()
    {
        var (truckList, manifests) = Repositories(AnEntry(), AManifest(Vin, frozen: true));

        var outcome = await new RemoveBoxAllocationHandler(truckList, manifests)
            .HandleAsync(new RemoveBoxAllocationCommand(ConvoyId, Vin, BoxId), TestContext.Current.CancellationToken);

        outcome.Should().Be(RemoveBoxAllocationOutcome.Frozen);
        await truckList.DidNotReceive().RemoveBoxAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removing_cargo_from_a_vehicle_that_is_not_on_the_convoy_says_so()
    {
        var (truckList, manifests) = Repositories(entry: null);

        var outcome = await new RemoveBoxAllocationHandler(truckList, manifests)
            .HandleAsync(new RemoveBoxAllocationCommand(ConvoyId, Vin, BoxId), TestContext.Current.CancellationToken);

        outcome.Should().Be(RemoveBoxAllocationOutcome.VehicleNotOnConvoy);
    }
}
