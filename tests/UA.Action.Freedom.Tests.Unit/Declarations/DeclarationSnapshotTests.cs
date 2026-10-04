using System.Text.Json;
using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Declarations;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Declarations;

/// <summary>
/// The snapshot a declaration is written from (ADR 0005): reading the load, storing it when the
/// declaration is ready to file, and reading it back from an older or newer writer.
/// </summary>
public class DeclarationSnapshotTests
{
    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly Guid ReceiverRef = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid ItemId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static LoadSnapshot ALoad() => new(
        LoadSnapshot.CurrentVersion, Vin, false,
        [new LoadBox(5, 20, ReceiverRef, true, [new LoadItem(ItemId, 3, 10, 25m, "9919000000")])]);

    // --- reading the load -------------------------------------------------------------------------

    private static VehicleLoadReader AReader(
        out IConvoyVehicleRepository truckList, ReceiverStatus receiverStatus = ReceiverStatus.Registered)
    {
        truckList = Substitute.For<IConvoyVehicleRepository>();
        truckList.GetAsync(42, Vin, Arg.Any<CancellationToken>())
            .Returns(new ConvoyVehicleReadModel(Vin, "AB12 CDE", 3000, 2, 0));
        truckList.ListBoxesAsync(42, Vin, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ManifestBoxReadModel>?>([new ManifestBoxReadModel(5, 20, true)]);

        var boxes = Substitute.For<IBoxRepository>();
        boxes.GetByIdAsync(5, Arg.Any<CancellationToken>())
            .Returns(new BoxReadModel(5, 20, null, null, null, ReceiverRef, null, null, null));
        boxes.ListItemsAsync(5, Arg.Any<CancellationToken>()).Returns<IReadOnlyList<BoxItemReadModel>>(
            [new BoxItemReadModel(ItemId, "Bandages", new Dictionary<string, string>(), 3, "9919000000", 10, 25m)]);

        var receivers = Substitute.For<IReceiverRepository>();
        receivers.GetByRefAsync(ReceiverRef, Arg.Any<CancellationToken>())
            .Returns(new ReceiverReadModel(ReceiverRef, "Hospital 4", "Lviv", receiverStatus));

        return new VehicleLoadReader(truckList, boxes, receivers);
    }

    [Fact]
    public async Task Reads_the_vehicles_boxes_with_their_items_and_receivers()
    {
        var load = await AReader(out _).ReadAsync(42, Vin, TestContext.Current.CancellationToken);

        load.Should().NotBeNull();
        Staleness.IsStale(ALoad(), load!).Should().BeFalse();
        load!.Boxes.Should().ContainSingle().Which.ReceiverRegistered.Should().BeTrue();
    }

    [Fact]
    public async Task Reads_a_receiver_that_is_not_registered_as_such()
    {
        var load = await AReader(out _, ReceiverStatus.Suspended).ReadAsync(42, Vin, TestContext.Current.CancellationToken);

        load!.Boxes.Single().ReceiverRegistered.Should().BeFalse();
    }

    [Fact]
    public async Task Reads_a_withdrawn_vehicle_as_withdrawn()
    {
        var reader = AReader(out var truckList);
        truckList.GetAsync(42, Vin, Arg.Any<CancellationToken>())
            .Returns(new ConvoyVehicleReadModel(Vin, "AB12 CDE", 3000, 2, 0, DateTime.UtcNow, "breakdown"));

        var load = await reader.ReadAsync(42, Vin, TestContext.Current.CancellationToken);

        load!.VehicleWithdrawn.Should().BeTrue();
    }

    [Fact]
    public async Task Reads_nothing_for_a_vehicle_that_is_not_on_the_convoy()
    {
        var reader = AReader(out var truckList);
        truckList.GetAsync(42, Vin, Arg.Any<CancellationToken>()).Returns((ConvoyVehicleReadModel?)null);

        (await reader.ReadAsync(42, Vin, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    // --- storage format ---------------------------------------------------------------------------

    [Fact]
    public void A_snapshot_survives_a_round_trip_through_its_stored_form()
    {
        var restored = LoadSnapshotJson.Read(LoadSnapshotJson.Write(ALoad()), LoadSnapshot.CurrentVersion);

        Staleness.IsStale(ALoad(), restored!).Should().BeFalse();
    }

    [Fact]
    public void A_stored_snapshot_with_a_field_this_code_does_not_know_is_still_read()
    {
        var withExtra = LoadSnapshotJson.Write(ALoad()).Replace("\"vin\"", "\"fieldFromTheFuture\":1,\"vin\"");

        Staleness.IsStale(ALoad(), LoadSnapshotJson.Read(withExtra, LoadSnapshot.CurrentVersion)!).Should().BeFalse();
    }

    [Fact]
    public void Nothing_is_read_from_a_row_without_a_snapshot() =>
        LoadSnapshotJson.Read(null, null).Should().BeNull();

    [Fact]
    public void A_stored_snapshot_carries_no_name_address_or_contact() =>
        JsonDocument.Parse(LoadSnapshotJson.Write(ALoad())).RootElement.GetRawText()
            .Should().NotContainAny("address", "contact", "phone", "organisation", "region");

    // --- marking ready ----------------------------------------------------------------------------

    private static (MarkDeclarationReadyHandler Handler, IDeclarationRepository Repository) AReadyHandler(
        MarkReadyResult result = MarkReadyResult.Ready, LoadSnapshot? load = null)
    {
        var repository = Substitute.For<IDeclarationRepository>();
        repository.MarkReadyAsync(
                Arg.Any<int>(), Arg.Any<string>(), Arg.Any<DeclarationKind>(), Arg.Any<Guid?>(),
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(result);
        var loads = Substitute.For<IVehicleLoadReader>();
        loads.ReadAsync(42, Vin, Arg.Any<CancellationToken>()).Returns(load);
        return (new MarkDeclarationReadyHandler(repository, loads), repository);
    }

    [Fact]
    public async Task Marking_ready_stores_the_load_it_was_written_from()
    {
        var (handler, repository) = AReadyHandler(load: ALoad());

        var outcome = await handler.HandleAsync(
            new MarkDeclarationReadyCommand(42, Vin, DeclarationKind.Gmr), TestContext.Current.CancellationToken);

        outcome.Should().Be(MarkDeclarationReadyOutcome.Ready);
        await repository.Received(1).MarkReadyAsync(
            42, Vin, DeclarationKind.Gmr, null, LoadSnapshotJson.Write(ALoad()), LoadSnapshot.CurrentVersion,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Marking_ready_a_vehicle_that_is_not_on_the_convoy_stores_nothing()
    {
        var (handler, repository) = AReadyHandler(load: null);

        var outcome = await handler.HandleAsync(
            new MarkDeclarationReadyCommand(42, Vin, DeclarationKind.Gmr), TestContext.Current.CancellationToken);

        outcome.Should().Be(MarkDeclarationReadyOutcome.VehicleNotOnConvoy);
        await repository.DidNotReceive().MarkReadyAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<DeclarationKind>(), Arg.Any<Guid?>(),
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Marking_ready_is_refused_once_the_declaration_has_been_filed()
    {
        var (handler, _) = AReadyHandler(MarkReadyResult.NotPreparable, ALoad());

        var outcome = await handler.HandleAsync(
            new MarkDeclarationReadyCommand(42, Vin, DeclarationKind.Gmr), TestContext.Current.CancellationToken);

        outcome.Should().Be(MarkDeclarationReadyOutcome.NotPreparable);
    }

    [Fact]
    public async Task A_goods_list_needs_the_receiver_it_is_for()
    {
        var (handler, _) = AReadyHandler(load: ALoad());

        var outcome = await handler.HandleAsync(
            new MarkDeclarationReadyCommand(42, Vin, DeclarationKind.GoodsList), TestContext.Current.CancellationToken);

        outcome.Should().Be(MarkDeclarationReadyOutcome.ReceiverRequired);
    }

    [Fact]
    public async Task Only_a_goods_list_names_a_receiver()
    {
        var (handler, _) = AReadyHandler(load: ALoad());

        var outcome = await handler.HandleAsync(
            new MarkDeclarationReadyCommand(42, Vin, DeclarationKind.Gmr, ReceiverRef), TestContext.Current.CancellationToken);

        outcome.Should().Be(MarkDeclarationReadyOutcome.ReceiverNotAllowed);
    }

    // --- stamping a declaration recorded without being marked ready -------------------------------

    [Fact]
    public async Task A_declaration_recorded_straight_to_filed_is_stamped_with_the_load_at_that_moment()
    {
        var repository = Substitute.For<IDeclarationRepository>();
        var loads = Substitute.For<IVehicleLoadReader>();
        loads.ReadAsync(42, Vin, Arg.Any<CancellationToken>()).Returns(ALoad());

        await new DeclarationSnapshots(repository, loads)
            .StampAsync(42, Vin, DeclarationKind.Gmr, null, TestContext.Current.CancellationToken);

        await repository.Received(1).StoreSnapshotAsync(
            42, Vin, DeclarationKind.Gmr, null, LoadSnapshotJson.Write(ALoad()), LoadSnapshot.CurrentVersion,
            Arg.Any<CancellationToken>());
    }
}
