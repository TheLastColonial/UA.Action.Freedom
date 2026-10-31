using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Declarations;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>The facts loader: it reads each port and hands the rules plain facts.</summary>
public class GetConvoyReadinessHandlerTests
{
    private static VehicleInsuranceReadModel APolicy(DateTime coverEnd) => new(
        ConvoyTestData.Id, "VIN-1", "Acme", "P-1", ConvoyTestData.Start.AddDays(-7), coverEnd, null, Guid.Empty,
        ConvoyTestData.Start.AddDays(-10), null);

    private static DeclarationReadModel ADeclaration(DeclarationKind kind, DeclarationStatus status, Guid? receiver = null) =>
        new(1, ConvoyTestData.Id, "VIN-1", kind, status, receiver, "REF", null, null, null, null, null);

    private static ReadinessWorld AWorldWithOneVehicle(int drivers = 2) =>
        new ReadinessWorld().WithVehicle(ConvoyTestData.AVehicle("VIN-1", drivers) with { HandoverReceiverRef = ReadinessWorld.Receiver });

    private static BoxReadModel ABox(Guid? receiver) =>
        new(7, 20, null, null, null, receiver, null, null, ConvoyTestData.Start);

    private static Requirement Find(ConvoyReadinessReadModel? readiness, string code, string? vin = null) =>
        readiness!.Requirements.Single(requirement => requirement.Code == code && requirement.Vin == vin);

    [Fact]
    public async Task There_is_no_readiness_for_a_convoy_that_does_not_exist()
    {
        var world = new ReadinessWorld();
        world.Convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns((ConvoyReadModel?)null);

        (await world.ReadAsync()).Should().BeNull();
    }

    [Fact]
    public async Task A_convoy_with_an_open_leader_assignment_has_a_leader()
    {
        var world = AWorldWithOneVehicle();
        world.Leaders.HistoryAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(
        [
            new ConvoyLeaderAssignmentReadModel(1, ConvoyTestData.Id, Guid.NewGuid(), "A Leader", ConvoyTestData.Start.AddDays(-2), null),
        ]);

        Find(await world.ReadAsync(), "leader").State.Should().Be(RequirementState.Done);
    }

    [Fact]
    public async Task A_leader_whose_assignment_has_closed_is_no_leader()
    {
        var world = AWorldWithOneVehicle();
        world.Leaders.HistoryAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(
        [
            new ConvoyLeaderAssignmentReadModel(
                1, ConvoyTestData.Id, Guid.NewGuid(), "A Leader", ConvoyTestData.Start.AddDays(-5), ConvoyTestData.Start.AddDays(-2)),
        ]);

        Find(await world.ReadAsync(), "leader").State.Should().Be(RequirementState.ToDo);
    }

    [Fact]
    public async Task The_handover_receiver_must_be_registered()
    {
        var registered = AWorldWithOneVehicle().WithReceiver(ReadinessWorld.Receiver, ReceiverStatus.Registered);
        var suspended = AWorldWithOneVehicle().WithReceiver(ReadinessWorld.Receiver, ReceiverStatus.Suspended);

        Find(await registered.ReadAsync(), "handover-receiver", "VIN-1").State.Should().Be(RequirementState.Done);
        Find(await suspended.ReadAsync(), "handover-receiver", "VIN-1").State.Should().Be(RequirementState.ToDo);
    }

    [Fact]
    public async Task A_vehicle_with_no_handover_receiver_fails_the_requirement()
    {
        var world = new ReadinessWorld().WithVehicle(ConvoyTestData.AVehicle("VIN-1"));

        Find(await world.ReadAsync(), "handover-receiver", "VIN-1").State.Should().Be(RequirementState.ToDo);
    }

    [Fact]
    public async Task An_allocated_box_is_checked_for_its_receivers_registration()
    {
        var world = AWorldWithOneVehicle().WithReceiver(ReadinessWorld.Receiver, ReceiverStatus.Pending);
        world.TruckList.ListBoxesAsync(ConvoyTestData.Id, "VIN-1", Arg.Any<CancellationToken>())
            .Returns([new ManifestBoxReadModel(7, 20, Validated: true)]);
        world.Boxes.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(ABox(ReadinessWorld.Receiver));
        world.Boxes.ListItemsAsync(7, Arg.Any<CancellationToken>()).Returns([]);

        Find(await world.ReadAsync(), "boxes", "VIN-1").State.Should().Be(RequirementState.ToDo);
    }

    [Fact]
    public async Task Staleness_is_already_in_the_status_the_declarations_query_reports()
    {
        var world = AWorldWithOneVehicle();
        world.Declarations.HandleAsync(Arg.Any<ListDeclarationsQuery>(), Arg.Any<CancellationToken>()).Returns(
            (IReadOnlyList<DeclarationReadModel>?)[ADeclaration(DeclarationKind.Gmr, DeclarationStatus.Stale)]);

        Find(await world.ReadAsync(), "declaration.gmr", "VIN-1").State.Should().Be(RequirementState.ToDo);
    }

    [Fact]
    public async Task A_withdrawn_vehicle_is_not_read_at_all()
    {
        var world = new ReadinessWorld().WithVehicle(ConvoyTestData.AWithdrawnVehicle("VIN-9"));

        var readiness = await world.ReadAsync();

        readiness!.Requirements.Should().NotContain(requirement => requirement.Vin == "VIN-9");
        await world.Declarations.DidNotReceive().HandleAsync(Arg.Any<ListDeclarationsQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Items_without_a_value_or_donor_and_expired_items_reach_the_advisories()
    {
        var world = AWorldWithOneVehicle();
        world.TruckList.ListBoxesAsync(ConvoyTestData.Id, "VIN-1", Arg.Any<CancellationToken>())
            .Returns([new ManifestBoxReadModel(7, 20, Validated: true)]);
        world.Boxes.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(ABox(null));
        world.Boxes.ListItemsAsync(7, Arg.Any<CancellationToken>()).Returns(
        [
            new BoxItemReadModel(Guid.NewGuid(), "tins", new Dictionary<string, string>(), 1, ExpiresOn: new DateOnly(2026, 1, 1)),
        ]);

        var readiness = await world.ReadAsync(now: new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        Find(readiness, "advisory.item-incomplete", "VIN-1").State.Should().Be(RequirementState.Warning);
        Find(readiness, "advisory.shelf-life", "VIN-1").State.Should().Be(RequirementState.Warning);
    }

    [Fact]
    public async Task Insurance_is_judged_on_the_later_of_now_and_the_convoy_start_so_readiness_and_departure_agree()
    {
        var world = AWorldWithOneVehicle().WithPolicy("VIN-1", APolicy(ConvoyTestData.Start.AddDays(2)));

        var early = await world.ReadAsync(now: ConvoyTestData.Start.AddDays(-10));
        var late = await world.ReadAsync(now: ConvoyTestData.Start.AddDays(5));

        Find(early, "insurance", "VIN-1").State.Should().Be(RequirementState.Done);
        Find(late, "insurance", "VIN-1").State.Should().Be(RequirementState.ToDo);
    }
}
