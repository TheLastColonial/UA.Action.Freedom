using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// ADR 0005 from the outside: a declaration stores the load it was written from and is stale whenever the
/// load now differs. Nothing sets the flag, so moving a box is enough to make both vehicles' declarations
/// read Stale.
/// </summary>
public class DeclarationStalenessEndpointTests
{
    private const int ConvoyId = 42;
    private const string VinA = "WVWZZZ1JZXW000001";
    private const string VinB = "WVWZZZ1JZXW000002";
    private const int BoxId = 3;
    private static readonly Guid ReceiverRef = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Published = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);

    private static string Declarations(string vin) => $"/convoys/{ConvoyId}/vehicles/{vin}/declarations";

    private static string BoxOn(string vin) => $"/convoys/{ConvoyId}/vehicles/{vin}/boxes/{BoxId}";

    private sealed record World(
        WebApplicationFactory<Program> Api,
        InMemoryReceiverRepository Receivers,
        InMemoryBoxRepository Boxes) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Api.DisposeAsync();
    }

    /// <summary>Two vehicles on one convoy and a registered Receiver; one box with one item, not yet on a vehicle.</summary>
    private static World AWorld(params string[] roles)
    {
        var convoys = new InMemoryConvoyRepository(new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), Published))
            .WithVehicle(VinA, onConvoy: ConvoyId)
            .WithVehicle(VinB, onConvoy: ConvoyId)
            .WithKnownBox(new ManifestBoxReadModel(BoxId, 12, Validated: true));
        var receivers = new InMemoryReceiverRepository(
            new ReceiverReadModel(ReceiverRef, "Hospital 4", "Lviv", ReceiverStatus.Registered));
        var boxes = new InMemoryBoxRepository(
                new BoxReadModel(BoxId, 12, null, null, null, ReceiverRef, null, null, null))
            .WithItem(
                BoxId,
                new BoxItemReadModel(Guid.NewGuid(), "Bandages", new Dictionary<string, string>(), 3, Quantity: 10));

        var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(), convoys, new InMemoryPersonRepository().CalledByALinkedVolunteer(),
            new RecordingManifestWorkQueue(), boxes: boxes, receivers: receivers, roles: roles);

        return new World(api, receivers, boxes);
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string vin) =>
        await client.GetFromJsonAsync<JsonElement>(Declarations(vin), TestContext.Current.CancellationToken);

    private static string? StatusOf(JsonElement list, string kind) =>
        list.EnumerateArray().FirstOrDefault(d => d.GetProperty("kind").GetString() == kind)
            is { ValueKind: JsonValueKind.Object } declaration
            ? declaration.GetProperty("status").GetString()
            : null;

    private static Task<HttpResponseMessage> RecordGmr(HttpClient client, string vin, string reference = "GMR-1") =>
        client.PostAsJsonAsync(
            $"{Declarations(vin)}/gmr/record", new { reference }, TestContext.Current.CancellationToken);

    // ---- increment 2: marking a declaration ready --------------------------------------------------

    [Fact]
    public async Task A_dispatcher_marks_a_declaration_ready_and_it_reads_ready_to_file()
    {
        await using var world = AWorld("Dispatcher");
        using var client = world.Api.CreateClient();

        var response = await client.PostAsync(
            $"{Declarations(VinA)}/gmr/ready", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        StatusOf(await ListAsync(client, VinA), "Gmr").Should().Be("ReadyToFile");
    }

    [Fact]
    public async Task The_stored_snapshot_is_never_part_of_the_json()
    {
        await using var world = AWorld("Dispatcher");
        using var client = world.Api.CreateClient();
        await client.PostAsync($"{Declarations(VinA)}/gmr/ready", content: null, TestContext.Current.CancellationToken);

        var declaration = (await ListAsync(client, VinA)).EnumerateArray().Single();

        declaration.EnumerateObject().Select(property => property.Name.ToLowerInvariant())
            .Should().NotContain(["snapshotjson", "snapshotversion", "snapshot"]);
    }

    [Fact]
    public async Task A_declaration_that_has_been_filed_cannot_be_marked_ready_again()
    {
        await using var world = AWorld("Dispatcher");
        using var client = world.Api.CreateClient();
        await RecordGmr(client, VinA);

        var response = await client.PostAsync(
            $"{Declarations(VinA)}/gmr/ready", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_goods_list_is_marked_ready_for_the_receiver_it_is_for()
    {
        await using var world = AWorld("Dispatcher");
        using var client = world.Api.CreateClient();

        var without = await client.PostAsync(
            $"{Declarations(VinA)}/goods-list/ready", content: null, TestContext.Current.CancellationToken);
        var with = await client.PostAsync(
            $"{Declarations(VinA)}/goods-list/ready?receiverRef={ReceiverRef}", content: null,
            TestContext.Current.CancellationToken);

        without.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        with.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Marking_a_declaration_ready_on_a_vehicle_not_on_the_convoy_is_not_found()
    {
        await using var world = AWorld("Dispatcher");
        using var client = world.Api.CreateClient();

        var response = await client.PostAsync(
            $"/convoys/{ConvoyId}/vehicles/NOSUCHVIN000000/declarations/gmr/ready", content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("GroundOfficer", HttpStatusCode.Forbidden)]
    [InlineData("Loader", HttpStatusCode.Forbidden)]
    public async Task Only_those_who_declare_may_mark_a_declaration_ready(string role, HttpStatusCode expected)
    {
        await using var world = AWorld(role);
        using var client = world.Api.CreateClient();

        var response = await client.PostAsync(
            $"{Declarations(VinA)}/gmr/ready", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(expected);
    }
}
