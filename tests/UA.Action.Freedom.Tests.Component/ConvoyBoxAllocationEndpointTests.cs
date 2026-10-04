using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// ADR 0004: a vehicle's cargo is the boxes allocated to its truck-list entry, and a box is on at
/// most one vehicle.
/// </summary>
public class ConvoyBoxAllocationEndpointTests
{
    private const int ConvoyId = 7;
    private const string VinA = "WVWZZZ1JZXW000001";
    private const string VinB = "WVWZZZ1JZXW000002";
    private const int BoxId = 3;

    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    private static InMemoryConvoyRepository AConvoyWithTwoVehicles() =>
        new InMemoryConvoyRepository(new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), null))
            .WithVehicle(VinA, onConvoy: ConvoyId)
            .WithVehicle(VinB, onConvoy: ConvoyId)
            .WithKnownBox(new ManifestBoxReadModel(BoxId, 12, Validated: true));

    private static string Route(string vin, int boxId = BoxId) => $"/convoys/{ConvoyId}/vehicles/{vin}/boxes/{boxId}";

    private static string List(string vin) => $"/convoys/{ConvoyId}/vehicles/{vin}/boxes";

    [Fact]
    public async Task A_loader_puts_a_box_on_a_vehicle_and_it_is_listed_as_its_cargo()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithTwoVehicles(), roles: "Loader");
        using var client = api.CreateClient();

        var put = await client.PutAsync(Route(VinA), content: null, TestContext.Current.CancellationToken);
        var list = await client.GetFromJsonAsync<JsonElement>(List(VinA), TestContext.Current.CancellationToken);

        put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        list.GetArrayLength().Should().Be(1);
        list[0].GetProperty("boxId").GetInt32().Should().Be(BoxId);
        list[0].GetProperty("weightKg").GetInt32().Should().Be(12);
    }

    [Fact]
    public async Task Putting_the_box_on_a_second_vehicle_moves_it()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithTwoVehicles(), roles: "Loader");
        using var client = api.CreateClient();
        await client.PutAsync(Route(VinA), content: null, TestContext.Current.CancellationToken);

        var move = await client.PutAsync(Route(VinB), content: null, TestContext.Current.CancellationToken);

        move.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<JsonElement>(List(VinA), TestContext.Current.CancellationToken))
            .GetArrayLength().Should().Be(0);
        (await client.GetFromJsonAsync<JsonElement>(List(VinB), TestContext.Current.CancellationToken))
            .GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Taking_a_box_off_a_vehicle_removes_it_from_the_cargo()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithTwoVehicles(), roles: "Loader");
        using var client = api.CreateClient();
        await client.PutAsync(Route(VinA), content: null, TestContext.Current.CancellationToken);

        var delete = await client.DeleteAsync(Route(VinA), TestContext.Current.CancellationToken);

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<JsonElement>(List(VinA), TestContext.Current.CancellationToken))
            .GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Taking_a_box_off_a_vehicle_it_is_not_on_is_a_404()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithTwoVehicles(), roles: "Loader");
        using var client = api.CreateClient();
        await client.PutAsync(Route(VinA), content: null, TestContext.Current.CancellationToken);

        var delete = await client.DeleteAsync(Route(VinB), TestContext.Current.CancellationToken);

        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_vehicle_that_is_not_on_the_convoy_or_a_box_that_does_not_exist_is_a_404()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithTwoVehicles(), roles: "Loader");
        using var client = api.CreateClient();

        (await client.PutAsync(Route("NOSUCHVIN000000"), content: null, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PutAsync(Route(VinA, boxId: 999), content: null, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(List("NOSUCHVIN000000"), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_withdrawn_vehicle_takes_no_new_cargo()
    {
        var convoys = new InMemoryConvoyRepository(new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), null))
            .WithWithdrawnVehicle(VinA, ConvoyId)
            .WithKnownBox(new ManifestBoxReadModel(BoxId, 12, Validated: true));
        await using var api = FreedomApi.WithConvoys(convoys, roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PutAsync(Route(VinA), content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        convoys.Allocations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_vehicle_whose_goods_movement_reference_exists_cannot_gain_or_lose_cargo()
    {
        // Transitional until plan 15: the GMR stamp still freezes the vehicle's load.
        var frozen = new ManifestReadModel(
            "MAN-1", ConvoyId, VinA, ManifestStatus.Confirmed, null, false,
            GmrSubmittedAt: new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc));
        var convoys = AConvoyWithTwoVehicles();
        await using var api = FreedomApi.WithConvoys(convoys, manifests: new InMemoryManifestRepository(frozen), roles: "Loader");
        using var client = api.CreateClient();

        (await client.PutAsync(Route(VinA), content: null, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Moving a box off the frozen vehicle changes its load just as surely.
        await client.PutAsync(Route(VinB), content: null, TestContext.Current.CancellationToken);
        (await client.PutAsync(Route(VinA), content: null, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        convoys.Allocations.Should().ContainSingle().Which.Vin.Should().Be(VinB);
    }

    [Fact]
    public async Task A_box_cannot_be_moved_off_a_vehicle_whose_goods_movement_reference_exists()
    {
        var frozen = new ManifestReadModel(
            "MAN-1", ConvoyId, VinA, ManifestStatus.Confirmed, null, false,
            GmrSubmittedAt: new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc));
        var convoys = AConvoyWithTwoVehicles();
        convoys.Ledger.Allocate(
            new ConvoyVehicle { ConvoyId = new ConvoyId(ConvoyId), Vin = VinA }, BoxId);
        await using var api = FreedomApi.WithConvoys(convoys, manifests: new InMemoryManifestRepository(frozen), roles: "Loader");
        using var client = api.CreateClient();

        (await client.PutAsync(Route(VinB), content: null, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.DeleteAsync(Route(VinA), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        convoys.Allocations.Should().ContainSingle().Which.Vin.Should().Be(VinA);
    }

    [Theory]
    [InlineData("Mechanic")]
    [InlineData("Purchaser")]
    [InlineData("GroundOfficer")]
    public async Task Roles_that_do_not_handle_boxes_cannot_allocate_them(string role)
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithTwoVehicles(), roles: role);
        using var client = api.CreateClient();

        var response = await client.PutAsync(Route(VinA), content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Roles_that_read_boxes_can_list_a_vehicles_cargo_and_the_ground_officer_cannot()
    {
        await using var dispatcher = FreedomApi.WithConvoys(AConvoyWithTwoVehicles(), roles: "Dispatcher");
        using var dispatcherClient = dispatcher.CreateClient();
        await using var groundOfficer = FreedomApi.WithConvoys(AConvoyWithTwoVehicles(), roles: "GroundOfficer");
        using var groundOfficerClient = groundOfficer.CreateClient();

        (await dispatcherClient.GetAsync(List(VinA), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await groundOfficerClient.GetAsync(List(VinA), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
