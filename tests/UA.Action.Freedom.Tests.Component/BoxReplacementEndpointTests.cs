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
/// ADR 0011 from the outside: an attested box is never edited or deleted. If its contents must change it is
/// voided and a new, unattested box takes its place with the same items.
/// </summary>
public class BoxReplacementEndpointTests
{
    private const int BoxId = 7;

    private const int ConvoyId = 42;

    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly Guid ReceiverRef = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid Loader = InMemoryPersonRepository.TestUserId;

    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Published = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);

    private static BoxReadModel ABox(bool validated = true) => new(
        BoxId,
        WeightKg: validated ? 24 : 0,
        WidthCm: null,
        DepthCm: null,
        HeightCm: null,
        ReceiverRef: null,
        LocationId: null,
        ValidatedByPersonId: validated ? Loader : null,
        ValidatedAt: validated ? new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc) : null);

    private static BoxItemReadModel AnItem() => new(
        Guid.NewGuid(), "Blankets", new Dictionary<string, string> { ["size"] = "double" },
        InMemoryItemCategoryRepository.OtherId, Quantity: 4, ValueGbp: 12.5m, ValueSource: ValueSource.Donor,
        ExpiresOn: new DateOnly(2030, 1, 1), DonationId: null);

    private static InMemoryBoxRepository APackedBox(bool validated = true) =>
        new InMemoryBoxRepository(ABox(validated)).WithItem(BoxId, AnItem());

    private static Task<HttpResponseMessage> Replace(HttpClient client, int id = BoxId) =>
        client.PostAsync($"/boxes/{id}/replace", content: null, TestContext.Current.CancellationToken);

    private static async Task<JsonElement> ReadBox(HttpClient client, int id) =>
        await client.GetFromJsonAsync<JsonElement>($"/boxes/{id}", TestContext.Current.CancellationToken);

    private static async Task<int> NewBoxId(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
        .GetProperty("boxId").GetInt32();

    private static async Task<string?> ProblemType(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
        .GetProperty("type").GetString();

    [Fact]
    public async Task An_administrator_replaces_an_attested_box_and_the_replacement_is_unattested()
    {
        await using var api = FreedomApi.WithBoxes(
            APackedBox(), InMemoryPersonRepository.WithLinkedTestUser(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await Replace(client);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var replacementId = await NewBoxId(response);
        replacementId.Should().NotBe(BoxId);
        var replacement = await ReadBox(client, replacementId);
        replacement.GetProperty("validated").GetBoolean().Should().BeFalse();
        replacement.GetProperty("weightKg").GetInt32().Should().Be(0);
        replacement.GetProperty("replacesBoxId").GetInt32().Should().Be(BoxId);
    }

    [Fact]
    public async Task The_old_box_is_voided_and_points_forward_to_its_replacement()
    {
        await using var api = FreedomApi.WithBoxes(
            APackedBox(), InMemoryPersonRepository.WithLinkedTestUser(), roles: "Administrator");
        using var client = api.CreateClient();

        var replacementId = await NewBoxId(await Replace(client));

        var old = await ReadBox(client, BoxId);
        old.GetProperty("voided").GetBoolean().Should().BeTrue();
        old.GetProperty("validated").GetBoolean().Should().BeTrue("the record of what was attested is never rewritten");
        old.GetProperty("replacedByBoxId").GetInt32().Should().Be(replacementId);
    }

    [Fact]
    public async Task The_items_are_copied_with_their_category_quantity_value_and_expiry()
    {
        var boxes = APackedBox();
        await using var api = FreedomApi.WithBoxes(
            boxes, InMemoryPersonRepository.WithLinkedTestUser(), roles: "Administrator");
        using var client = api.CreateClient();

        var replacementId = await NewBoxId(await Replace(client));

        var copied = boxes.Items(replacementId).Should().ContainSingle().Subject;
        var original = boxes.Items(BoxId).Should().ContainSingle().Subject;
        copied.Id.Should().NotBe(original.Id);
        copied.Should().BeEquivalentTo(original, options => options.Excluding(item => item.Id));
    }

    [Fact]
    public async Task The_old_label_stops_resolving()
    {
        var token = Guid.NewGuid();
        var boxes = APackedBox().WithQrCode(new BoxQrCodeReadModel(token, BoxId, DateTime.UtcNow, RevokedAt: null));
        await using var api = FreedomApi.WithBoxes(
            boxes, InMemoryPersonRepository.WithLinkedTestUser(), roles: "Administrator");
        using var client = api.CreateClient();

        await Replace(client);

        var scan = await client.GetAsync($"/boxes/scan/{token}", TestContext.Current.CancellationToken);
        scan.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_box_nobody_has_attested_is_edited_not_replaced()
    {
        await using var api = FreedomApi.WithBoxes(
            APackedBox(validated: false), InMemoryPersonRepository.WithLinkedTestUser(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await Replace(client);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemType(response)).Should().Be("box-not-attested");
    }

    [Fact]
    public async Task A_box_can_be_replaced_only_once()
    {
        var boxes = APackedBox();
        await using var api = FreedomApi.WithBoxes(
            boxes, InMemoryPersonRepository.WithLinkedTestUser(), roles: "Administrator");
        using var client = api.CreateClient();
        await Replace(client);

        var second = await Replace(client);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemType(second)).Should().Be("box-voided");
        boxes.Count.Should().Be(2);
    }

    [Fact]
    public async Task Replacing_a_box_that_does_not_exist_is_not_found()
    {
        await using var api = FreedomApi.WithBoxes(
            new InMemoryBoxRepository(), InMemoryPersonRepository.WithLinkedTestUser(), roles: "Administrator");
        using var client = api.CreateClient();

        (await Replace(client)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_ground_officer_cannot_replace_a_box()
    {
        await using var api = FreedomApi.WithBoxes(
            APackedBox(), InMemoryPersonRepository.WithLinkedTestUser(), roles: "GroundOfficer");
        using var client = api.CreateClient();

        (await Replace(client)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_voided_box_cannot_be_given_a_new_label()
    {
        await using var api = FreedomApi.WithBoxes(
            APackedBox(), InMemoryPersonRepository.WithLinkedTestUser(), roles: "Administrator");
        using var client = api.CreateClient();
        await Replace(client);

        var response = await client.PostAsync(
            $"/boxes/{BoxId}/qr-code", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- the ripple: cargo moves, declarations go stale ------------------------------------------------

    private sealed record World(WebApplicationFactory<Program> Api, InMemoryConvoyRepository Convoys) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Api.DisposeAsync();
    }

    private static World ALoadedVehicle()
    {
        var convoys = new InMemoryConvoyRepository(new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), Published))
            .WithVehicle(Vin, onConvoy: ConvoyId)
            .WithKnownBox(new ManifestBoxReadModel(BoxId, 24, Validated: true));
        var receivers = new InMemoryReceiverRepository(
            new ReceiverReadModel(ReceiverRef, "Hospital 4", "Lviv", ReceiverStatus.Registered));
        var boxes = new InMemoryBoxRepository(ABox() with { ReceiverRef = ReceiverRef })
            .WithItem(BoxId, AnItem());

        var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(), convoys, InMemoryPersonRepository.WithLinkedTestUser(),
            new RecordingManifestWorkQueue(), boxes: boxes, receivers: receivers, roles: "Administrator");

        return new World(api, convoys);
    }

    private static Task<HttpResponseMessage> PutOnVehicle(HttpClient client) =>
        client.PutAsync(
            $"/convoys/{ConvoyId}/vehicles/{Vin}/boxes/{BoxId}", content: null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_cargo_moves_to_the_replacement_and_the_voided_box_carries_nothing()
    {
        await using var world = ALoadedVehicle();
        using var client = world.Api.CreateClient();
        (await PutOnVehicle(client)).IsSuccessStatusCode.Should().BeTrue();

        var replacementId = await NewBoxId(await Replace(client));

        var cargo = await client.GetFromJsonAsync<JsonElement>(
            $"/convoys/{ConvoyId}/vehicles/{Vin}/boxes", TestContext.Current.CancellationToken);
        cargo.EnumerateArray().Select(box => box.GetProperty("boxId").GetInt32())
            .Should().Equal(replacementId);
    }

    [Fact]
    public async Task A_voided_box_cannot_be_put_back_on_a_vehicle()
    {
        await using var world = ALoadedVehicle();
        using var client = world.Api.CreateClient();
        await Replace(client);

        var response = await PutOnVehicle(client);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Replacing_a_box_on_a_vehicle_makes_its_filed_declaration_stale()
    {
        await using var world = ALoadedVehicle();
        using var client = world.Api.CreateClient();
        await PutOnVehicle(client);
        await client.PostAsJsonAsync(
            $"/convoys/{ConvoyId}/vehicles/{Vin}/declarations/gmr/record",
            new { reference = "GMR-1" }, TestContext.Current.CancellationToken);

        await Replace(client);

        var declarations = await client.GetFromJsonAsync<JsonElement>(
            $"/convoys/{ConvoyId}/vehicles/{Vin}/declarations", TestContext.Current.CancellationToken);
        declarations.EnumerateArray().Single(d => d.GetProperty("kind").GetString() == "Gmr")
            .GetProperty("status").GetString().Should().Be("Stale");
    }
}
