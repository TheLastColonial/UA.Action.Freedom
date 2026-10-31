using AwesomeAssertions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Data.Boxes;
using UA.Action.Freedom.Data.Convoys;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.Convoys.ConvoyFixtures;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Boxes;

/// <summary>
/// How a box ended on a convoy, against a real <c>dbo.BoxOutcome</c>: one current row, history kept, the race settled by
/// the database, and a return to a hub that takes the box off its vehicle and puts it at the hub.
/// </summary>
[Trait("Category", "Integration")]
public class BoxOutcomeRepositoryTests
{
    private const string Probe = "SELECT COUNT(1) FROM dbo.BoxOutcome";

    private sealed record World(
        BoxOutcomeRepository Outcomes,
        ConvoyVehicleRepository TruckList,
        int ConvoyId,
        string Vin,
        int BoxId,
        int HubId);

    private static async Task<World> ArrangeAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync(Probe, cancellationToken);
        await EnsureRecorderAsync();

        var convoys = new ConvoyRepository(ConnectionFactory(), Unattributed);
        var truckList = new ConvoyVehicleRepository(ConnectionFactory(), Unattributed);
        var boxes = new BoxRepository(ConnectionFactory(), Unattributed);

        var convoyId = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        await AddVehicleAsync(vin);
        await truckList.AddAsync(convoyId, vin, cancellationToken);

        var boxId = await boxes.AddAsync(new BoxReadModel(0, 12, null, null, null, null, null, null, null), cancellationToken);
        await truckList.AllocateBoxAsync(convoyId, vin, boxId, cancellationToken);

        var hubId = await ScalarAsync(
            "INSERT INTO dbo.Location (Name, IsRegisteredHub) VALUES ('Integration Hub', 1); SELECT CAST(SCOPE_IDENTITY() AS int);");

        return new World(new BoxOutcomeRepository(ConnectionFactory(), AttributedTo(RecorderId)), truckList, convoyId, vin, boxId, hubId);
    }

    private static async Task CleanUpAsync(World world)
    {
        await ExecuteAsync("DELETE FROM dbo.BoxOutcome WHERE ConvoyId = @c", ("@c", world.ConvoyId));
        await ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @id", ("@id", world.BoxId));
        await ExecuteAsync("DELETE FROM dbo.Location WHERE Id = @id", ("@id", world.HubId));
        await RemoveVehicleAsync(world.Vin);
        await RemoveConvoyAsync(world.ConvoyId);
    }

    private static RecordBoxOutcome Record(
        World world, BoxOutcome? from, BoxOutcome outcome, string? reason = null, int? hub = null) =>
        new(world.ConvoyId, world.Vin, world.BoxId, from, outcome, reason, hub);

    [Fact]
    public async Task A_box_has_one_current_outcome_and_the_history_is_kept()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            (await world.Outcomes.RecordAsync(Record(world, null, BoxOutcome.Delivered), cancellationToken))
                .Should().Be(RecordBoxOutcomeResult.Recorded);
            (await world.Outcomes.RecordAsync(Record(world, BoxOutcome.Delivered, BoxOutcome.Accepted), cancellationToken))
                .Should().Be(RecordBoxOutcomeResult.Recorded);

            var current = await world.Outcomes.GetCurrentAsync(world.ConvoyId, world.BoxId, cancellationToken);
            current!.Outcome.Should().Be(BoxOutcome.Accepted);
            current.Vin.Should().Be(world.Vin);
            (await world.Outcomes.ListCurrentAsync(world.ConvoyId, cancellationToken)).Should().ContainSingle();
            (await ScalarAsync("SELECT COUNT(1) FROM dbo.BoxOutcome WHERE BoxId = @b", ("@b", world.BoxId))).Should().Be(2);
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task A_writer_that_saw_an_older_outcome_conflicts_and_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            await world.Outcomes.RecordAsync(Record(world, null, BoxOutcome.Delivered), cancellationToken);

            (await world.Outcomes.RecordAsync(Record(world, null, BoxOutcome.Seized), cancellationToken))
                .Should().Be(RecordBoxOutcomeResult.Conflict);
            (await world.Outcomes.RecordAsync(Record(world, BoxOutcome.Accepted, BoxOutcome.Seized), cancellationToken))
                .Should().Be(RecordBoxOutcomeResult.Conflict);

            (await world.Outcomes.GetCurrentAsync(world.ConvoyId, world.BoxId, cancellationToken))!.Outcome
                .Should().Be(BoxOutcome.Delivered);
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task A_seized_box_leaves_the_vehicles_load()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            (await world.TruckList.ListBoxesAsync(world.ConvoyId, world.Vin, cancellationToken))!.Should().ContainSingle();

            await world.Outcomes.RecordAsync(Record(world, null, BoxOutcome.Seized), cancellationToken);

            (await world.TruckList.ListBoxesAsync(world.ConvoyId, world.Vin, cancellationToken)).Should().BeEmpty();
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task An_undeliverable_box_keeps_its_reason_and_leaves_the_load()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            await world.Outcomes.RecordAsync(Record(world, null, BoxOutcome.Undeliverable, "stolen"), cancellationToken);

            (await world.Outcomes.GetCurrentAsync(world.ConvoyId, world.BoxId, cancellationToken))!.Reason.Should().Be("stolen");
            (await world.TruckList.ListBoxesAsync(world.ConvoyId, world.Vin, cancellationToken)).Should().BeEmpty();
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task A_box_returned_to_a_hub_is_at_the_hub_and_off_its_vehicle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            await world.Outcomes.RecordAsync(Record(world, null, BoxOutcome.ReturnedToHub, hub: world.HubId), cancellationToken);

            (await world.TruckList.GetBoxAllocationAsync(world.BoxId, cancellationToken)).Should().BeNull();
            (await ScalarAsync("SELECT LocationId FROM dbo.Box WHERE Id = @b", ("@b", world.BoxId))).Should().Be(world.HubId);
            (await world.Outcomes.GetCurrentAsync(world.ConvoyId, world.BoxId, cancellationToken))!.HubLocationId
                .Should().Be(world.HubId);
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task Accepting_a_goods_list_accepts_only_that_receivers_delivered_boxes_in_one_go()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);
        var boxes = new BoxRepository(ConnectionFactory(), Unattributed);
        var otherBoxId = await boxes.AddAsync(new BoxReadModel(0, 8, null, null, null, null, null, null, null), cancellationToken);
        var receiverA = Guid.NewGuid();
        var receiverB = Guid.NewGuid();

        try
        {
            await world.TruckList.AllocateBoxAsync(world.ConvoyId, world.Vin, otherBoxId, cancellationToken);
            await ExecuteAsync(
                "INSERT INTO dbo.Receiver (ReceiverRef, Organisation, Region) VALUES (@a, 'A', 'Lviv'), (@b, 'B', 'Kyiv')",
                ("@a", receiverA), ("@b", receiverB));
            await ExecuteAsync("UPDATE dbo.Box SET ReceiverRef = @r WHERE Id = @id", ("@r", receiverA), ("@id", world.BoxId));
            await ExecuteAsync("UPDATE dbo.Box SET ReceiverRef = @r WHERE Id = @id", ("@r", receiverB), ("@id", otherBoxId));
            await world.Outcomes.RecordAsync(Record(world, null, BoxOutcome.Delivered), cancellationToken);
            await world.Outcomes.RecordAsync(
                new RecordBoxOutcome(world.ConvoyId, world.Vin, otherBoxId, null, BoxOutcome.Delivered, null, null), cancellationToken);

            var accepted = await world.Outcomes.AcceptGoodsListAsync(world.ConvoyId, world.Vin, receiverA, cancellationToken);

            accepted.Should().Be(1);
            (await world.Outcomes.GetCurrentAsync(world.ConvoyId, world.BoxId, cancellationToken))!.Outcome.Should().Be(BoxOutcome.Accepted);
            (await world.Outcomes.GetCurrentAsync(world.ConvoyId, otherBoxId, cancellationToken))!.Outcome.Should().Be(BoxOutcome.Delivered);
            (await world.Outcomes.AcceptGoodsListAsync(world.ConvoyId, world.Vin, receiverA, cancellationToken))
                .Should().Be(0, "accepting again finds nothing delivered");
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.BoxOutcome WHERE ConvoyId = @c", ("@c", world.ConvoyId));
            await ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @id", ("@id", otherBoxId));
            await CleanUpAsync(world);
            await ExecuteAsync("DELETE FROM dbo.Receiver WHERE ReceiverRef IN (@a, @b)", ("@a", receiverA), ("@b", receiverB));
        }
    }

    [Fact]
    public async Task Delivering_a_vehicle_stamps_it_once_and_delivers_the_boxes_that_had_no_outcome()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            (await world.Outcomes.DeliverVehicleAsync(world.ConvoyId, world.Vin, cancellationToken)).Should().Be(1);

            (await world.TruckList.GetAsync(world.ConvoyId, world.Vin, cancellationToken))!.Delivered.Should().BeTrue();
            (await world.Outcomes.GetCurrentAsync(world.ConvoyId, world.BoxId, cancellationToken))!.Outcome
                .Should().Be(BoxOutcome.Delivered);
            (await world.Outcomes.DeliverVehicleAsync(world.ConvoyId, world.Vin, cancellationToken))
                .Should().BeNull("a vehicle is delivered once, and nothing is written the second time");
            (await ScalarAsync("SELECT COUNT(1) FROM dbo.BoxOutcome WHERE BoxId = @b", ("@b", world.BoxId))).Should().Be(1);
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task Delivering_a_vehicle_leaves_a_box_that_already_has_an_outcome_alone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            await world.Outcomes.RecordAsync(Record(world, null, BoxOutcome.Seized), cancellationToken);

            (await world.Outcomes.DeliverVehicleAsync(world.ConvoyId, world.Vin, cancellationToken)).Should().Be(0);

            (await world.Outcomes.GetCurrentAsync(world.ConvoyId, world.BoxId, cancellationToken))!.Outcome
                .Should().Be(BoxOutcome.Seized);
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }
}
