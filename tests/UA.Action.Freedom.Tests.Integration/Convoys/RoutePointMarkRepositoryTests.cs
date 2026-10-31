using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Data.Convoys;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.Convoys.ConvoyFixtures;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Convoys;

/// <summary>
/// Progress marks against a real <c>dbo.RoutePointMark</c>: one mark per point, kind and vehicle; a crossing that closes the
/// accepted declarations of that border for the vehicles still travelling in the same transaction; and a mark that pins
/// its route point (ADR 0016).
/// </summary>
[Trait("Category", "Integration")]
public class RoutePointMarkRepositoryTests
{
    private const string Probe = "SELECT COUNT(1) FROM dbo.RoutePointMark";

    private sealed record World(
        RoutePointMarkRepository Marks,
        ConvoyRepository Convoys,
        int ConvoyId,
        string Vin,
        string BrokenDownVin,
        int HubPoint,
        int BorderPoint,
        Guid Leader);

    private static async Task<World> ArrangeAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync(Probe, cancellationToken);
        var leader = await AddDriverAsync("Lena", "Leader");
        var convoys = new ConvoyRepository(ConnectionFactory(), Unattributed);
        var truckList = new ConvoyVehicleRepository(ConnectionFactory(), Unattributed);

        var convoyId = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var brokenDownVin = NewVin();
        await AddVehicleAsync(vin);
        await AddVehicleAsync(brokenDownVin);
        await truckList.AddAsync(convoyId, vin, cancellationToken);
        await truckList.AddAsync(convoyId, brokenDownVin, cancellationToken);
        await ExecuteAsync(
            "UPDATE dbo.ConvoyVehicle SET WithdrawnAt = SYSUTCDATETIME(), WithdrawnReason = 'Gearbox' WHERE ConvoyId = @c AND Vin = @v",
            ("@c", convoyId), ("@v", brokenDownVin));

        await convoys.ReplaceRouteAsync(
            convoyId,
            [
                new RouteStopReadModel(1, null, null, "Coventry", "UK", "", "GB", 0, "Coventry hub", RoutePointKind.Hub),
                new RouteStopReadModel(2, null, null, "Calais", "France", "", "FR", 0, "Calais", RoutePointKind.Border, CustomsAuthority.UK),
            ],
            cancellationToken);
        var route = await convoys.GetRouteAsync(convoyId, cancellationToken);

        return new World(
            new RoutePointMarkRepository(ConnectionFactory(), AttributedTo(leader)),
            convoys, convoyId, vin, brokenDownVin, route[0].RoutePointId, route[1].RoutePointId, leader);
    }

    private static async Task CleanUpAsync(World world)
    {
        await RemoveConvoyAsync(world.ConvoyId);
        await RemoveVehicleAsync(world.Vin);
        await RemoveVehicleAsync(world.BrokenDownVin);
        await RemovePeopleAsync(world.Leader);
    }

    private static RoutePointMark AMark(
        World world, int point, RoutePointMarkKind kind = RoutePointMarkKind.Reached, string? vin = null) =>
        new(world.ConvoyId, point, kind, vin, world.Leader, DateTime.UtcNow, OnBehalfOfLeader: false);

    private static Task SeedAsync(World world, string vin, DeclarationKind kind, DeclarationStatus status) => ExecuteAsync(
        "INSERT INTO dbo.Declaration (ConvoyId, Vin, Kind, Status) VALUES (@c, @v, @k, @s)",
        ("@c", world.ConvoyId), ("@v", vin), ("@k", (int)kind), ("@s", (int)status));

    private static async Task<DeclarationStatus> StatusAsync(World world, string vin, DeclarationKind kind) =>
        (DeclarationStatus)await ScalarAsync(
            "SELECT Status FROM dbo.Declaration WHERE ConvoyId = @c AND Vin = @v AND Kind = @k",
            ("@c", world.ConvoyId), ("@v", vin), ("@k", (int)kind));

    [Fact]
    public async Task A_mark_is_read_back_with_the_person_who_entered_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            (await world.Marks.RecordAsync(AMark(world, world.HubPoint), [], cancellationToken))
                .Should().Be(RecordMarkResult.Recorded);

            var marks = await world.Marks.ListAsync(world.ConvoyId, cancellationToken);
            marks.Should().ContainSingle();
            marks[0].RoutePointId.Should().Be(world.HubPoint);
            marks[0].Kind.Should().Be(RoutePointMarkKind.Reached);
            marks[0].RecordedByName.Should().Be("Lena Leader");
            marks[0].OnBehalfOfLeader.Should().BeFalse();
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task A_second_mark_for_the_same_point_kind_and_vehicle_is_refused_and_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            await world.Marks.RecordAsync(AMark(world, world.HubPoint), [], cancellationToken);
            await world.Marks.RecordAsync(AMark(world, world.HubPoint, vin: world.Vin), [], cancellationToken);

            (await world.Marks.RecordAsync(AMark(world, world.HubPoint), [], cancellationToken))
                .Should().Be(RecordMarkResult.AlreadyMarked);
            (await world.Marks.RecordAsync(AMark(world, world.HubPoint, vin: world.Vin), [], cancellationToken))
                .Should().Be(RecordMarkResult.AlreadyMarked);

            (await world.Marks.ListAsync(world.ConvoyId, cancellationToken)).Should().HaveCount(2);
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task A_crossing_closes_the_accepted_declarations_of_that_border_for_vehicles_still_travelling()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            await SeedAsync(world, world.Vin, DeclarationKind.Gmr, DeclarationStatus.Accepted);
            await SeedAsync(world, world.Vin, DeclarationKind.Ens, DeclarationStatus.Accepted);
            await SeedAsync(world, world.BrokenDownVin, DeclarationKind.Gmr, DeclarationStatus.Accepted);

            await world.Marks.RecordAsync(
                AMark(world, world.BorderPoint, RoutePointMarkKind.Crossed),
                RoutePointMarks.DeclarationsClosedBy(CustomsAuthority.UK),
                cancellationToken);

            (await StatusAsync(world, world.Vin, DeclarationKind.Gmr)).Should().Be(DeclarationStatus.Closed);
            (await StatusAsync(world, world.Vin, DeclarationKind.Ens)).Should().Be(DeclarationStatus.Accepted);
            (await StatusAsync(world, world.BrokenDownVin, DeclarationKind.Gmr)).Should().Be(DeclarationStatus.Accepted);
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task Only_an_accepted_declaration_closes_and_a_crossing_for_one_vehicle_closes_only_its_own()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            await SeedAsync(world, world.Vin, DeclarationKind.Gmr, DeclarationStatus.Filed);

            await world.Marks.RecordAsync(
                AMark(world, world.BorderPoint, RoutePointMarkKind.Crossed),
                RoutePointMarks.DeclarationsClosedBy(CustomsAuthority.UK),
                cancellationToken);

            (await StatusAsync(world, world.Vin, DeclarationKind.Gmr)).Should().Be(DeclarationStatus.Filed);
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task A_refused_mark_does_not_close_anything()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            await SeedAsync(world, world.Vin, DeclarationKind.Gmr, DeclarationStatus.Accepted);
            var crossed = AMark(world, world.BorderPoint, RoutePointMarkKind.Crossed);
            await world.Marks.RecordAsync(crossed, [], cancellationToken);

            (await world.Marks.RecordAsync(crossed, [DeclarationKind.Gmr], cancellationToken))
                .Should().Be(RecordMarkResult.AlreadyMarked);

            (await StatusAsync(world, world.Vin, DeclarationKind.Gmr)).Should().Be(DeclarationStatus.Accepted);
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task A_marked_route_point_is_in_use_and_an_unmarked_one_is_not()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            var references = new AccommodationRoutePointReferences(ConnectionFactory());
            await world.Marks.RecordAsync(AMark(world, world.HubPoint), [], cancellationToken);

            (await references.AnyAsync(world.ConvoyId, [world.HubPoint], cancellationToken)).Should().BeTrue();
            (await references.AnyAsync(world.ConvoyId, [world.BorderPoint], cancellationToken)).Should().BeFalse();
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }

    [Fact]
    public async Task A_mark_cannot_name_a_vehicle_that_is_not_on_the_convoy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ArrangeAsync(cancellationToken);

        try
        {
            var act = () => world.Marks.RecordAsync(AMark(world, world.HubPoint, vin: NewVin()), [], cancellationToken);

            await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
        }
        finally
        {
            await CleanUpAsync(world);
        }
    }
}
