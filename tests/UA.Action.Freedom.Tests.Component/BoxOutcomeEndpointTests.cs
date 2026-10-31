using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// ADR 0015: how a box ended on a convoy. The Dispatcher records it for the Convoy Leader (plan 14), a seized or
/// undeliverable box leaves the vehicle's load, and a box returned to a registered hub is reusable.
/// </summary>
public class BoxOutcomeEndpointTests
{
    private const int ConvoyId = 42;
    private const string VinA = "WVWZZZ1JZXW000001";
    private const int NextConvoyId = 43;
    private const string VinB = "WVWZZZ1JZXW000002";
    private const int BoxId = 3;
    private const int OtherBoxId = 4;
    private static readonly Guid OtherReceiver = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const int Hub = 10;
    private const int NotAHub = 11;
    private static readonly Guid ReceiverRef = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Published = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);

    private static string OutcomeOf(int boxId = BoxId) => $"/convoys/{ConvoyId}/boxes/{boxId}/outcome";

    private sealed record World(
        WebApplicationFactory<Program> Api,
        InMemoryConvoyRepository Convoys,
        InMemoryBoxRepository Boxes) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Api.DisposeAsync();
    }

    private static World AWorld(bool departed = true, params string[] roles)
    {
        var convoys = new InMemoryConvoyRepository(
                new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), Published, DepartedAt: departed ? Departs : null),
                new ConvoyReadModel(NextConvoyId, Departs.AddDays(30), Departs.AddDays(34), Published))
            .WithVehicle(VinA, onConvoy: ConvoyId)
            .WithVehicle(VinB, onConvoy: NextConvoyId)
            .WithKnownBox(new ManifestBoxReadModel(BoxId, 12, Validated: true))
            .WithKnownBox(new ManifestBoxReadModel(OtherBoxId, 8, Validated: true));
        var receivers = new InMemoryReceiverRepository(
            new ReceiverReadModel(ReceiverRef, "Hospital 4", "Lviv", ReceiverStatus.Registered));
        var boxes = new InMemoryBoxRepository(
                new BoxReadModel(BoxId, 12, null, null, null, ReceiverRef, null, null, null),
                new BoxReadModel(OtherBoxId, 8, null, null, null, OtherReceiver, null, null, null))
            .WithItem(BoxId, new BoxItemReadModel(Guid.NewGuid(), "Bandages", new Dictionary<string, string>(), 3, Quantity: 10));
        var locations = new InMemoryLocationRepository(
            new LocationReadModel(Hub, "Coventry hub", null, null, null, null, null, IsRegisteredHub: true),
            new LocationReadModel(NotAHub, "Somebody's garage", null, null, null, null, null));

        var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(), convoys, new InMemoryPersonRepository().CalledByALinkedVolunteer(),
            new RecordingManifestWorkQueue(), boxes: boxes, receivers: receivers, locations: locations, roles: roles);

        return new World(api, convoys, boxes);
    }

    private static async Task<HttpClient> LoadedAsync(World world)
    {
        var client = world.Api.CreateClient();
        var allocated = await client.PutAsync(
            $"/convoys/{ConvoyId}/vehicles/{VinA}/boxes/{BoxId}", content: null, TestContext.Current.CancellationToken);
        allocated.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.Created);
        return client;
    }

    private static Task<HttpResponseMessage> Record(
        HttpClient client, string outcome, string? reason = null, int? hubLocationId = null) =>
        client.PostAsJsonAsync(OutcomeOf(), new { outcome, reason, hubLocationId }, TestContext.Current.CancellationToken);

    private static async Task<string?> ProblemTypeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
        .GetProperty("type").GetString();

    [Fact]
    public async Task A_dispatcher_records_a_box_as_delivered()
    {
        await using var world = AWorld(roles: "Dispatcher");
        using var client = await LoadedAsync(world);

        var response = await Record(client, "Delivered");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("boxId").GetInt32().Should().Be(BoxId);
        body.GetProperty("outcome").GetString().Should().Be("Delivered");
    }

    [Fact]
    public async Task A_delivered_box_is_accepted_once_and_a_second_outcome_after_that_is_refused()
    {
        await using var world = AWorld(roles: "Dispatcher");
        using var client = await LoadedAsync(world);
        await Record(client, "Delivered");

        (await Record(client, "Accepted")).StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await Record(client, "Seized");
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemTypeAsync(again)).Should().Be("box-outcome-illegal");
    }

    [Fact]
    public async Task A_box_cannot_be_accepted_before_it_is_delivered()
    {
        await using var world = AWorld(roles: "Dispatcher");
        using var client = await LoadedAsync(world);

        var response = await Record(client, "Accepted");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemTypeAsync(response)).Should().Be("box-outcome-illegal");
    }

    [Fact]
    public async Task No_outcome_is_recorded_before_the_convoy_departs()
    {
        await using var world = AWorld(departed: false, roles: "Dispatcher");
        using var client = await LoadedAsync(world);

        var response = await Record(client, "Delivered");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemTypeAsync(response)).Should().Be("convoy-not-departed");
    }

    [Fact]
    public async Task A_box_that_is_not_on_the_convoy_is_not_found()
    {
        await using var world = AWorld(roles: "Dispatcher");
        using var client = world.Api.CreateClient();

        (await Record(client, "Delivered")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unknown_convoy_is_not_found()
    {
        await using var world = AWorld(roles: "Dispatcher");
        using var client = world.Api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/convoys/999/boxes/{BoxId}/outcome", new { outcome = "Delivered" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_undeliverable_box_needs_one_of_the_bounded_reasons()
    {
        await using var world = AWorld(roles: "Dispatcher");
        using var client = await LoadedAsync(world);

        var missing = await Record(client, "Undeliverable", "left it at 12 Some Street");
        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ProblemTypeAsync(missing)).Should().Be("box-outcome-reason-required");

        (await Record(client, "Undeliverable", "stolen")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_box_cannot_be_returned_to_a_location_that_is_not_a_registered_hub()
    {
        await using var world = AWorld(roles: "Dispatcher");
        using var client = await LoadedAsync(world);

        foreach (var hub in new int?[] { null, NotAHub, 999 })
        {
            var response = await Record(client, "ReturnedToHub", hubLocationId: hub);
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ProblemTypeAsync(response)).Should().Be("hub-not-registered");
        }
    }

    [Fact]
    public async Task A_seized_box_leaves_the_vehicles_load_and_its_filed_declarations_go_stale()
    {
        await using var world = AWorld(roles: "Dispatcher");
        using var client = await LoadedAsync(world);
        var declarations = $"/convoys/{ConvoyId}/vehicles/{VinA}/declarations";
        await client.PostAsJsonAsync($"{declarations}/gmr/record", new { reference = "GMR-1" }, TestContext.Current.CancellationToken);

        (await Record(client, "Seized")).StatusCode.Should().Be(HttpStatusCode.OK);

        var cargo = await client.GetFromJsonAsync<JsonElement>(
            $"/convoys/{ConvoyId}/vehicles/{VinA}/boxes", TestContext.Current.CancellationToken);
        cargo.GetArrayLength().Should().Be(0);
        var list = await client.GetFromJsonAsync<JsonElement>(declarations, TestContext.Current.CancellationToken);
        list.EnumerateArray().Single(d => d.GetProperty("kind").GetString() == "Gmr")
            .GetProperty("status").GetString().Should().Be("Stale");
    }

    [Fact]
    public async Task A_box_returned_to_a_registered_hub_is_at_the_hub_and_off_the_vehicle()
    {
        await using var world = AWorld(roles: "Dispatcher");
        using var client = await LoadedAsync(world);

        (await Record(client, "ReturnedToHub", hubLocationId: Hub)).StatusCode.Should().Be(HttpStatusCode.OK);

        world.Boxes.Box(BoxId)!.LocationId.Should().Be(Hub);
        world.Convoys.Allocations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_box_returned_to_a_hub_can_be_allocated_to_a_later_convoy_and_travel_again()
    {
        await using var world = AWorld(roles: "Dispatcher");
        using var client = await LoadedAsync(world);
        await Record(client, "ReturnedToHub", hubLocationId: Hub);

        var allocated = await client.PutAsync(
            $"/convoys/{NextConvoyId}/vehicles/{VinB}/boxes/{BoxId}", content: null, TestContext.Current.CancellationToken);

        allocated.IsSuccessStatusCode.Should().BeTrue();
        var cargo = await client.GetFromJsonAsync<JsonElement>(
            $"/convoys/{NextConvoyId}/vehicles/{VinB}/boxes", TestContext.Current.CancellationToken);
        cargo.EnumerateArray().Select(box => box.GetProperty("boxId").GetInt32()).Should().Equal(BoxId);
        world.Boxes.Box(BoxId)!.ReceiverRef.Should().Be(ReceiverRef, "the box is unchanged: same receiver, same label, same contents");
        world.Boxes.Items(BoxId).Should().ContainSingle();
    }

    // ---- increment 4: acceptance per goods list ----------------------------------------------------

    private static string Accepted(Guid receiver) =>
        $"/convoys/{ConvoyId}/vehicles/{VinA}/declarations/goods-list/{receiver}/accepted";

    [Fact]
    public async Task Accepting_a_goods_list_accepts_every_delivered_box_for_that_receiver_and_no_other()
    {
        await using var world = AWorld(roles: "Dispatcher");
        using var client = await LoadedAsync(world);
        await client.PutAsync($"/convoys/{ConvoyId}/vehicles/{VinA}/boxes/{OtherBoxId}", null, TestContext.Current.CancellationToken);
        await Record(client, "Delivered");
        await client.PostAsJsonAsync(OutcomeOf(OtherBoxId), new { outcome = "Delivered" }, TestContext.Current.CancellationToken);

        var response = await client.PostAsync(Accepted(ReceiverRef), null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("boxesAccepted").GetInt32().Should().Be(1);
        world.Convoys.Ledger.CurrentOutcome(ConvoyId, BoxId)!.Outcome.Should().Be(BoxOutcome.Accepted);
        world.Convoys.Ledger.CurrentOutcome(ConvoyId, OtherBoxId)!.Outcome.Should().Be(BoxOutcome.Delivered);
    }

    [Fact]
    public async Task A_box_that_was_seized_is_not_accepted_with_the_rest_of_its_list()
    {
        await using var world = AWorld(roles: "Dispatcher");
        using var client = await LoadedAsync(world);
        await Record(client, "Seized");

        var response = await client.PostAsync(Accepted(ReceiverRef), null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemTypeAsync(response)).Should().Be("goods-list-nothing-to-accept");
        world.Convoys.Ledger.CurrentOutcome(ConvoyId, BoxId)!.Outcome.Should().Be(BoxOutcome.Seized);
    }

    [Fact]
    public async Task A_goods_list_cannot_be_accepted_before_the_convoy_departs()
    {
        await using var world = AWorld(departed: false, roles: "Dispatcher");
        using var client = await LoadedAsync(world);

        var response = await client.PostAsync(Accepted(ReceiverRef), null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemTypeAsync(response)).Should().Be("convoy-not-departed");
    }

    [Fact]
    public async Task Accepting_a_goods_list_on_a_vehicle_that_is_not_on_the_convoy_is_not_found()
    {
        await using var world = AWorld(roles: "Dispatcher");
        using var client = world.Api.CreateClient();

        var response = await client.PostAsync(
            $"/convoys/{ConvoyId}/vehicles/{VinB}/declarations/goods-list/{ReceiverRef}/accepted",
            null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_loader_cannot_accept_a_goods_list()
    {
        await using var world = AWorld(roles: "Loader");
        using var client = world.Api.CreateClient();

        (await client.PostAsync(Accepted(ReceiverRef), null, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_loader_cannot_record_an_outcome()
    {
        await using var world = AWorld(roles: "Loader");
        using var client = world.Api.CreateClient();

        (await Record(client, "Delivered")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        var convoys = new InMemoryConvoyRepository();
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(), convoys, new InMemoryPersonRepository(), new RecordingManifestWorkQueue(),
            authenticated: false);
        using var client = api.CreateClient();

        (await Record(client, "Delivered")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
