using AwesomeAssertions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Data.Boxes;
using UA.Action.Freedom.Data.Convoys;
using static UA.Action.Freedom.Tests.Integration.Convoys.ConvoyFixtures;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Convoys;

/// <summary>
/// Cargo as a box allocated to a truck-list entry, against a real database: the primary key that
/// puts a box on at most one vehicle, the move that never leaves it on two or none, and the
/// cascades from the entry and from the box.
/// </summary>
[Trait("Category", "Integration")]
public class BoxAllocationRepositoryTests
{
    private static async Task<(ConvoyRepository Convoys, ConvoyVehicleRepository TruckList, BoxRepository Boxes)>
        ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync(Probe, cancellationToken);
        await EnsureRecorderAsync();
        return (
            new ConvoyRepository(ConnectionFactory(), Unattributed),
            new ConvoyVehicleRepository(ConnectionFactory(), Unattributed),
            new BoxRepository(ConnectionFactory(), Unattributed));
    }

    private static Task<int> NewBoxAsync(BoxRepository boxes, CancellationToken cancellationToken) =>
        boxes.AddAsync(new BoxReadModel(0, 12, null, null, null, null, null, null, null), cancellationToken);

    private static Task RemoveBoxAsync(int boxId) =>
        ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @id", ("@id", boxId));

    [Fact]
    public async Task A_box_is_on_one_vehicle_and_allocating_it_to_another_moves_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, boxes) = await ConnectOrSkipAsync(cancellationToken);
        var convoyId = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vinA = NewVin();
        var vinB = NewVin();
        var boxId = await NewBoxAsync(boxes, cancellationToken);

        try
        {
            await AddVehicleAsync(vinA);
            await AddVehicleAsync(vinB);
            await truckList.AddAsync(convoyId, vinA, cancellationToken);
            await truckList.AddAsync(convoyId, vinB, cancellationToken);

            (await truckList.AllocateBoxAsync(convoyId, vinA, boxId, cancellationToken)).Should().Be(AllocateBoxResult.Allocated);
            (await truckList.AllocateBoxAsync(convoyId, vinA, boxId, cancellationToken)).Should().Be(AllocateBoxResult.AlreadyAllocated);
            (await truckList.ListBoxesAsync(convoyId, vinA, cancellationToken))!.Select(b => b.BoxId).Should().Equal(boxId);

            (await truckList.AllocateBoxAsync(convoyId, vinB, boxId, cancellationToken)).Should().Be(AllocateBoxResult.Moved);

            (await truckList.ListBoxesAsync(convoyId, vinA, cancellationToken)).Should().BeEmpty();
            (await truckList.ListBoxesAsync(convoyId, vinB, cancellationToken))!.Select(b => b.BoxId).Should().Equal(boxId);
            (await ScalarAsync("SELECT COUNT(1) FROM dbo.ConvoyVehicleBoxAllocation WHERE BoxId = @b", ("@b", boxId)))
                .Should().Be(1);
            (await truckList.GetBoxAllocationAsync(boxId, cancellationToken))!.Vin.Should().Be(vinB);
        }
        finally
        {
            await RemoveBoxAsync(boxId);
            await RemoveVehicleAsync(vinA);
            await RemoveVehicleAsync(vinB);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task Removing_a_box_takes_it_off_only_the_vehicle_it_is_on()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, boxes) = await ConnectOrSkipAsync(cancellationToken);
        var convoyId = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vinA = NewVin();
        var vinB = NewVin();
        var boxId = await NewBoxAsync(boxes, cancellationToken);

        try
        {
            await AddVehicleAsync(vinA);
            await AddVehicleAsync(vinB);
            await truckList.AddAsync(convoyId, vinA, cancellationToken);
            await truckList.AddAsync(convoyId, vinB, cancellationToken);
            await truckList.AllocateBoxAsync(convoyId, vinA, boxId, cancellationToken);

            (await truckList.RemoveBoxAsync(convoyId, vinB, boxId, cancellationToken)).Should().BeFalse();
            (await truckList.RemoveBoxAsync(convoyId, vinA, boxId, cancellationToken)).Should().BeTrue();
            (await truckList.GetBoxAllocationAsync(boxId, cancellationToken)).Should().BeNull();
        }
        finally
        {
            await RemoveBoxAsync(boxId);
            await RemoveVehicleAsync(vinA);
            await RemoveVehicleAsync(vinB);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task A_withdrawn_vehicle_missing_entry_or_missing_box_takes_no_allocation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, boxes) = await ConnectOrSkipAsync(cancellationToken);
        var convoyId = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var boxId = await NewBoxAsync(boxes, cancellationToken);

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(convoyId, vin, cancellationToken);

            (await truckList.AllocateBoxAsync(convoyId, "NOSUCHVIN000000", boxId, cancellationToken))
                .Should().Be(AllocateBoxResult.VehicleNotOnConvoy);
            (await truckList.AllocateBoxAsync(convoyId, vin, int.MaxValue, cancellationToken))
                .Should().Be(AllocateBoxResult.BoxNotFound);
            (await truckList.ListBoxesAsync(convoyId, "NOSUCHVIN000000", cancellationToken)).Should().BeNull();

            await truckList.WithdrawAsync(convoyId, vin, "Accident", DateTime.UtcNow, cancellationToken);

            (await truckList.AllocateBoxAsync(convoyId, vin, boxId, cancellationToken))
                .Should().Be(AllocateBoxResult.VehicleWithdrawn);
            (await truckList.GetBoxAllocationAsync(boxId, cancellationToken)).Should().BeNull();
        }
        finally
        {
            await RemoveBoxAsync(boxId);
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task Removing_the_entry_or_the_box_takes_the_allocation_with_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (convoys, truckList, boxes) = await ConnectOrSkipAsync(cancellationToken);
        var convoyId = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var onEntry = await NewBoxAsync(boxes, cancellationToken);
        var onBox = await NewBoxAsync(boxes, cancellationToken);

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(convoyId, vin, cancellationToken);
            await truckList.AllocateBoxAsync(convoyId, vin, onEntry, cancellationToken);
            await truckList.AllocateBoxAsync(convoyId, vin, onBox, cancellationToken);

            await RemoveBoxAsync(onBox);
            (await truckList.ListBoxesAsync(convoyId, vin, cancellationToken))!.Select(b => b.BoxId).Should().Equal(onEntry);

            await truckList.RemoveAsync(convoyId, vin, cancellationToken);
            (await truckList.GetBoxAllocationAsync(onEntry, cancellationToken)).Should().BeNull();
        }
        finally
        {
            await RemoveBoxAsync(onEntry);
            await RemoveBoxAsync(onBox);
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }
}
