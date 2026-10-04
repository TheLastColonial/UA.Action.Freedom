using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// The convoy's route — the ordered list of stops from UK departure to Ukrainian delivery.
/// </summary>
/// <remarks>
/// Order is the whole point of a route, so the handler renumbers the stops it is given into a
/// dense 1..n sequence rather than trusting the caller's numbering. Two stops claiming the same
/// position, or a gap in the middle, would otherwise be stored and read back as a different
/// journey from the one the dispatcher entered.
/// </remarks>
public class ConvoyRouteHandlerTests
{
    private static readonly RouteStopReadModel[] TwoStops =
    [
        ConvoyTestData.AStop(1, "CV1 2AB"),
        ConvoyTestData.AStop(2, "80-180"),
    ];

    [Fact]
    public async Task Replaces_the_route_of_a_convoy_that_exists()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.ExistsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new ReplaceConvoyRouteHandler(repository, Substitute.For<IRoutePointReferences>());

        var outcome = await handler.HandleAsync(
            new ReplaceConvoyRouteCommand(ConvoyTestData.Id, TwoStops), CancellationToken.None);

        outcome.Should().Be(ReplaceConvoyRouteOutcome.Replaced);
        await repository.Received(1).ReplaceRouteAsync(
            ConvoyTestData.Id,
            Arg.Is<IReadOnlyList<RouteStopReadModel>>(stops => stops.Count == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_not_found_and_writes_nothing_when_there_is_no_such_convoy()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.ExistsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new ReplaceConvoyRouteHandler(repository, Substitute.For<IRoutePointReferences>());

        var outcome = await handler.HandleAsync(
            new ReplaceConvoyRouteCommand(ConvoyTestData.Id, TwoStops), CancellationToken.None);

        outcome.Should().Be(ReplaceConvoyRouteOutcome.NotFound);
        await repository.DidNotReceive().ReplaceRouteAsync(
            Arg.Any<int>(), Arg.Any<IReadOnlyList<RouteStopReadModel>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Numbers_the_stops_in_the_order_they_were_given()
    {
        // The caller sends a list; its order is the journey. Whatever sequence numbers arrive,
        // what is stored is 1..n in list order.
        var repository = Substitute.For<IConvoyRepository>();
        repository.ExistsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new ReplaceConvoyRouteHandler(repository, Substitute.For<IRoutePointReferences>());

        RouteStopReadModel[] jumbled =
        [
            ConvoyTestData.AStop(99, "CV1 2AB"),
            ConvoyTestData.AStop(99, "80-180"),
            ConvoyTestData.AStop(7, "61-001"),
        ];

        await handler.HandleAsync(
            new ReplaceConvoyRouteCommand(ConvoyTestData.Id, jumbled), CancellationToken.None);

        await repository.Received(1).ReplaceRouteAsync(
            ConvoyTestData.Id,
            Arg.Is<IReadOnlyList<RouteStopReadModel>>(stops =>
                stops[0].Sequence == 1 && stops[0].Postcode == "CV1 2AB"
                && stops[1].Sequence == 2 && stops[1].Postcode == "80-180"
                && stops[2].Sequence == 3 && stops[2].Postcode == "61-001"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Clearing_the_route_is_allowed()
    {
        // A convoy being replanned may legitimately have no route for a while.
        var repository = Substitute.For<IConvoyRepository>();
        repository.ExistsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new ReplaceConvoyRouteHandler(repository, Substitute.For<IRoutePointReferences>());

        var outcome = await handler.HandleAsync(
            new ReplaceConvoyRouteCommand(ConvoyTestData.Id, []), CancellationToken.None);

        outcome.Should().Be(ReplaceConvoyRouteOutcome.Replaced);
        await repository.Received(1).ReplaceRouteAsync(
            ConvoyTestData.Id,
            Arg.Is<IReadOnlyList<RouteStopReadModel>>(stops => stops.Count == 0),
            Arg.Any<CancellationToken>());
    }

    private static (IConvoyRepository Repository, IRoutePointReferences References, ReplaceConvoyRouteHandler Handler)
        AConvoyWithRoute(params RouteStopReadModel[] existing)
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.ExistsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(true);
        repository.GetRouteAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RouteStopReadModel>>(existing));
        var references = Substitute.For<IRoutePointReferences>();
        return (repository, references, new ReplaceConvoyRouteHandler(repository, references));
    }

    [Fact]
    public async Task Refuses_a_point_id_that_does_not_belong_to_the_convoy_and_writes_nothing()
    {
        var (repository, _, handler) = AConvoyWithRoute(ConvoyTestData.AStop(1) with { RoutePointId = 10 });

        var outcome = await handler.HandleAsync(
            new ReplaceConvoyRouteCommand(ConvoyTestData.Id, [ConvoyTestData.AStop(1) with { RoutePointId = 99 }]),
            CancellationToken.None);

        outcome.Should().Be(ReplaceConvoyRouteOutcome.UnknownPoint);
        await repository.DidNotReceive().ReplaceRouteAsync(
            Arg.Any<int>(), Arg.Any<IReadOnlyList<RouteStopReadModel>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_to_remove_a_point_something_else_refers_to_and_writes_nothing()
    {
        var (repository, references, handler) = AConvoyWithRoute(
            ConvoyTestData.AStop(1) with { RoutePointId = 10 },
            ConvoyTestData.AStop(2) with { RoutePointId = 11 });
        references.AnyAsync(ConvoyTestData.Id, Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 11 })), Arg.Any<CancellationToken>())
            .Returns(true);

        var outcome = await handler.HandleAsync(
            new ReplaceConvoyRouteCommand(ConvoyTestData.Id, [ConvoyTestData.AStop(1) with { RoutePointId = 10 }]),
            CancellationToken.None);

        outcome.Should().Be(ReplaceConvoyRouteOutcome.PointInUse);
        await repository.DidNotReceive().ReplaceRouteAsync(
            Arg.Any<int>(), Arg.Any<IReadOnlyList<RouteStopReadModel>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reordering_keeps_every_point_and_asks_nothing_about_references()
    {
        var (repository, references, handler) = AConvoyWithRoute(
            ConvoyTestData.AStop(1) with { RoutePointId = 10 },
            ConvoyTestData.AStop(2) with { RoutePointId = 11 });

        var outcome = await handler.HandleAsync(
            new ReplaceConvoyRouteCommand(ConvoyTestData.Id,
            [
                ConvoyTestData.AStop(1) with { RoutePointId = 11 },
                ConvoyTestData.AStop(2) with { RoutePointId = 10 },
            ]),
            CancellationToken.None);

        outcome.Should().Be(ReplaceConvoyRouteOutcome.Replaced);
        await references.DidNotReceive().AnyAsync(
            Arg.Any<int>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>());
        await repository.Received(1).ReplaceRouteAsync(
            ConvoyTestData.Id,
            Arg.Is<IReadOnlyList<RouteStopReadModel>>(stops =>
                stops[0].RoutePointId == 11 && stops[0].Sequence == 1
                && stops[1].RoutePointId == 10 && stops[1].Sequence == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removing_a_point_nothing_refers_to_is_allowed()
    {
        var (repository, _, handler) = AConvoyWithRoute(
            ConvoyTestData.AStop(1) with { RoutePointId = 10 },
            ConvoyTestData.AStop(2) with { RoutePointId = 11 });

        var outcome = await handler.HandleAsync(
            new ReplaceConvoyRouteCommand(ConvoyTestData.Id, [ConvoyTestData.AStop(1) with { RoutePointId = 10 }]),
            CancellationToken.None);

        outcome.Should().Be(ReplaceConvoyRouteOutcome.Replaced);
        await repository.Received(1).ReplaceRouteAsync(
            ConvoyTestData.Id, Arg.Any<IReadOnlyList<RouteStopReadModel>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reading_the_route_of_an_unknown_convoy_returns_nothing()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.ExistsAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new GetConvoyRouteHandler(repository);

        var route = await handler.HandleAsync(
            new GetConvoyRouteQuery(ConvoyTestData.Id), CancellationToken.None);

        route.Should().BeNull();
    }
}
