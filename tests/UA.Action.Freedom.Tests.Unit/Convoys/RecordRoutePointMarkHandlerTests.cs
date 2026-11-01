using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// ADR 0016, O27: the Convoy Leader marks their convoy's points, and a Dispatcher may mark on their behalf, in which case
/// the record names the Dispatcher and says so. Only the time of entry is kept.
/// </summary>
public class RecordRoutePointMarkHandlerTests
{
    private const int Hub = 1;
    private const int Overnight = 2;
    private const int UkBorder = 3;
    private const int UaBorder = 4;
    private const string Vin = "VIN-1";

    private static readonly Guid Leader = Guid.Parse("11111111-aaaa-aaaa-aaaa-111111111111");
    private static readonly Guid Dispatcher = Guid.Parse("22222222-bbbb-bbbb-bbbb-222222222222");
    private static readonly DateTime Now = ConvoyTestData.Start.AddHours(5);

    private static RouteStopReadModel Point(int id, RoutePointKind kind, CustomsAuthority? authority = null) =>
        ConvoyTestData.AStop(id) with { RoutePointId = id, Name = $"Point {id}", Kind = kind, Authority = authority };

    private sealed class Setup
    {
        public IConvoyRepository Convoys { get; } = Substitute.For<IConvoyRepository>();
        public IConvoyVehicleRepository TruckList { get; } = Substitute.For<IConvoyVehicleRepository>();
        public IConvoyLeaderRepository Leaders { get; } = Substitute.For<IConvoyLeaderRepository>();
        public IRoutePointMarkRepository Marks { get; } = Substitute.For<IRoutePointMarkRepository>();
        public ICurrentPerson Caller { get; } = Substitute.For<ICurrentPerson>();

        public Setup(Guid? caller = null, bool departed = true)
        {
            Convoys.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
                .Returns(ConvoyTestData.APublishedConvoy() with { DepartedAt = departed ? ConvoyTestData.Start : null });
            Convoys.GetRouteAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(
            [
                Point(Hub, RoutePointKind.Hub),
                Point(Overnight, RoutePointKind.Overnight),
                Point(UkBorder, RoutePointKind.Border, CustomsAuthority.UK),
                Point(UaBorder, RoutePointKind.Border, CustomsAuthority.UA),
            ]);
            TruckList.GetAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns(ConvoyTestData.AVehicle(Vin));
            Leaders.IsCurrentLeaderAsync(ConvoyTestData.Id, Leader, Arg.Any<CancellationToken>()).Returns(true);
            Marks.RecordAsync(Arg.Any<RoutePointMark>(), Arg.Any<IReadOnlyList<DeclarationKind>>(), Arg.Any<CancellationToken>())
                .Returns(RecordMarkResult.Recorded);
            Caller.ResolveAsync(Arg.Any<CancellationToken>()).Returns(
                caller is { } id ? new CurrentPerson.Linked(id) : new CurrentPerson.NotLinked());
        }

        public Task<RecordRoutePointMarkOutcome> MarkAsync(int point, RoutePointMarkKind kind, string? vin = null) =>
            new RecordRoutePointMarkHandler(Convoys, TruckList, Leaders, Marks, Caller, new FixedClock(Now))
                .HandleAsync(new RecordRoutePointMarkCommand(ConvoyTestData.Id, point, kind, vin), TestContext.Current.CancellationToken);

        public RoutePointMark Written() =>
            Marks.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRoutePointMarkRepository.RecordAsync))
                .GetArguments().OfType<RoutePointMark>().Single();

        public IReadOnlyList<DeclarationKind> Closed() =>
            Marks.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRoutePointMarkRepository.RecordAsync))
                .GetArguments().OfType<IReadOnlyList<DeclarationKind>>().Single();
    }

    [Fact]
    public async Task The_leader_marks_a_point_reached_in_their_own_name()
    {
        var setup = new Setup(Leader);

        (await setup.MarkAsync(Overnight, RoutePointMarkKind.Reached)).Should().Be(RecordRoutePointMarkOutcome.Recorded);

        setup.Written().Should().Be(new RoutePointMark(
            ConvoyTestData.Id, Overnight, RoutePointMarkKind.Reached, null, Leader, Now, OnBehalfOfLeader: false));
    }

    [Fact]
    public async Task A_dispatcher_marking_is_recorded_as_acting_on_the_leaders_behalf_and_is_named()
    {
        var setup = new Setup(Dispatcher);

        await setup.MarkAsync(Overnight, RoutePointMarkKind.Reached);

        setup.Written().RecordedBy.Should().Be(Dispatcher);
        setup.Written().OnBehalfOfLeader.Should().BeTrue();
    }

    [Fact]
    public async Task Crossing_a_border_closes_the_declarations_of_that_authority()
    {
        var setup = new Setup(Leader);

        await setup.MarkAsync(UkBorder, RoutePointMarkKind.Crossed);

        setup.Closed().Should().BeEquivalentTo([DeclarationKind.Gmr]);
    }

    [Fact]
    public async Task Crossing_the_Ukrainian_border_closes_the_goods_lists_and_nothing_else()
    {
        var setup = new Setup(Leader);

        await setup.MarkAsync(UaBorder, RoutePointMarkKind.Crossed);

        setup.Closed().Should().BeEquivalentTo([DeclarationKind.GoodsList]);
    }

    [Fact]
    public async Task Reaching_a_border_closes_nothing()
    {
        var setup = new Setup(Leader);

        await setup.MarkAsync(UkBorder, RoutePointMarkKind.Reached);

        setup.Closed().Should().BeEmpty();
    }

    [Fact]
    public async Task Only_a_border_can_be_crossed()
    {
        var setup = new Setup(Leader);

        (await setup.MarkAsync(Overnight, RoutePointMarkKind.Crossed)).Should().Be(RecordRoutePointMarkOutcome.CrossingNeedsBorder);

        await setup.Marks.DidNotReceiveWithAnyArgs().RecordAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_mark_on_a_point_that_is_not_on_the_route_is_refused()
    {
        var setup = new Setup(Leader);

        (await setup.MarkAsync(99, RoutePointMarkKind.Reached)).Should().Be(RecordRoutePointMarkOutcome.RoutePointNotFound);
    }

    [Fact]
    public async Task Nothing_is_marked_before_the_convoy_departs()
    {
        var setup = new Setup(Leader, departed: false);

        (await setup.MarkAsync(Hub, RoutePointMarkKind.Reached)).Should().Be(RecordRoutePointMarkOutcome.ConvoyNotDeparted);
    }

    [Fact]
    public async Task A_mark_for_a_vehicle_that_has_left_the_convoy_is_refused()
    {
        var setup = new Setup(Leader);
        setup.TruckList.GetAsync(ConvoyTestData.Id, Vin, Arg.Any<CancellationToken>()).Returns(ConvoyTestData.AWithdrawnVehicle(Vin));

        (await setup.MarkAsync(Hub, RoutePointMarkKind.Reached, Vin)).Should().Be(RecordRoutePointMarkOutcome.VehicleNotOnConvoy);
    }

    [Fact]
    public async Task A_second_identical_mark_is_reported_as_already_made()
    {
        var setup = new Setup(Leader);
        setup.Marks.RecordAsync(Arg.Any<RoutePointMark>(), Arg.Any<IReadOnlyList<DeclarationKind>>(), Arg.Any<CancellationToken>())
            .Returns(RecordMarkResult.AlreadyMarked);

        (await setup.MarkAsync(Hub, RoutePointMarkKind.Reached)).Should().Be(RecordRoutePointMarkOutcome.AlreadyMarked);
    }

    [Fact]
    public async Task A_login_nobody_has_linked_marks_nothing()
    {
        var setup = new Setup(caller: null);

        (await setup.MarkAsync(Hub, RoutePointMarkKind.Reached)).Should().Be(RecordRoutePointMarkOutcome.LoginNotLinked);
    }
}
