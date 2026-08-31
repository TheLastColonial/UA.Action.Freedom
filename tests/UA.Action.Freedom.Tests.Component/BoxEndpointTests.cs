using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// The <c>/boxes</c> contract from the outside, and what validation freezes.
/// </summary>
/// <remarks>
/// A Loader opens the box, checks the contents and weighs it. That confirmed weight is what the
/// border check relies on, so once it exists the box cannot change — no items in or out, no new
/// receiver, no second validation. These tests are what stop a later change quietly reopening
/// it (docs/domain/key-concepts.md § Box).
/// </remarks>
public class BoxEndpointTests
{
    private const int BoxId = 7;

    private static readonly Guid Loader = new("2b9c1e40-7d8a-4c31-9f52-6a0b8d3e5c11");

    private static BoxReadModel ABox(bool validated = false) => new(
        BoxId,
        WeightKg: validated ? 24 : 0,
        ReceiverRef: null,
        House: "Unit 4",
        Street: "Cross Road",
        City: "Coventry",
        Country: "United Kingdom",
        Postcode: "CV1 2AB",
        ValidatedByPersonId: validated ? Loader : null,
        ValidatedAt: validated ? new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc) : null);

    private static InMemoryPersonRepository AKnownLoader() => new(
        new PersonReadModel(
            Loader, "Sam", "Whitfield",
            new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            null, IsDriver: false, Committed: false));

    private static object AnItemBody() => new
    {
        description = "Blankets",
        properties = new Dictionary<string, string> { ["size"] = "double" },
    };

    [Fact]
    public async Task Reading_boxes_without_a_token_is_unauthorized()
    {
        await using var api = FreedomApi.WithBoxes(
            new InMemoryBoxRepository(), AKnownLoader(), authenticated: false);
        using var client = api.CreateClient();

        var response = await client.GetAsync("/boxes", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_ground_officer_is_refused_the_cargo_list()
    {
        await using var api = FreedomApi.WithBoxes(
            new InMemoryBoxRepository(), AKnownLoader(), roles: "GroundOfficer");
        using var client = api.CreateClient();

        var response = await client.GetAsync("/boxes", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_loader_packs_a_box_that_starts_with_no_confirmed_weight()
    {
        var boxes = new InMemoryBoxRepository();
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/boxes",
            new { house = "Unit 4", street = "Cross Road", city = "Coventry", postcode = "CV1 2AB" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        boxes.Box(1)!.WeightKg.Should().Be(0);
        boxes.Box(1)!.Validated.Should().BeFalse();
    }

    [Fact]
    public async Task A_purchaser_may_read_boxes_but_not_pack_them()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Purchaser");
        using var client = api.CreateClient();

        (await client.GetAsync("/boxes", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var write = await client.PostAsJsonAsync(
            "/boxes", new { city = "Coventry" }, TestContext.Current.CancellationToken);

        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_dispatcher_may_pack_a_box_but_not_vouch_for_it()
    {
        // Packing and vouching are different acts. The validation record is what the charity's
        // assurance to a border rests on, and that belongs to whoever opened the box.
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Dispatcher");
        using var client = api.CreateClient();

        (await client.PostAsJsonAsync($"/boxes/{BoxId}/items", AnItemBody(), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var validate = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/validate",
            new { validatedByPersonId = Loader, weightKg = 24 },
            TestContext.Current.CancellationToken);

        validate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        boxes.Box(BoxId)!.Validated.Should().BeFalse();
    }

    [Fact]
    public async Task A_loader_validates_a_box_and_the_weight_becomes_authoritative()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/validate",
            new { validatedByPersonId = Loader, weightKg = 24 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var box = await client.GetFromJsonAsync<JsonElement>($"/boxes/{BoxId}", TestContext.Current.CancellationToken);
        box.GetProperty("validated").GetBoolean().Should().BeTrue();
        box.GetProperty("weightKg").GetInt32().Should().Be(24);
        box.GetProperty("validatedByPersonId").GetGuid().Should().Be(Loader);
    }

    [Fact]
    public async Task Validating_a_box_twice_is_a_conflict()
    {
        var boxes = new InMemoryBoxRepository(ABox(validated: true));
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/validate",
            new { validatedByPersonId = Loader, weightKg = 30 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        boxes.Box(BoxId)!.WeightKg.Should().Be(24);
    }

    [Fact]
    public async Task Naming_a_validator_who_is_not_on_file_is_a_404()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, new InMemoryPersonRepository(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/validate",
            new { validatedByPersonId = Loader, weightKg = 24 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        boxes.Box(BoxId)!.Validated.Should().BeFalse();
    }

    [Fact]
    public async Task A_box_validated_at_an_implausible_weight_is_rejected()
    {
        // A typo here would reach a border document as a fact somebody had signed for.
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/validate",
            new { validatedByPersonId = Loader, weightKg = 9_999 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        boxes.Box(BoxId)!.Validated.Should().BeFalse();
    }

    [Fact]
    public async Task Nothing_can_be_packed_into_a_validated_box()
    {
        var boxes = new InMemoryBoxRepository(ABox(validated: true));
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/items", AnItemBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        boxes.Items(BoxId).Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_can_be_unpacked_from_a_validated_box()
    {
        var item = new BoxItemReadModel(Guid.NewGuid(), "Blankets", new Dictionary<string, string>());
        var boxes = new InMemoryBoxRepository(ABox(validated: true)).WithItem(BoxId, item);
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync(
            $"/boxes/{BoxId}/items/{item.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        boxes.Items(BoxId).Should().ContainSingle();
    }

    [Fact]
    public async Task A_validated_box_cannot_be_pointed_at_another_receiver()
    {
        var boxes = new InMemoryBoxRepository(ABox(validated: true));
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/boxes/{BoxId}",
            new { receiverRef = Guid.NewGuid(), city = "Lviv" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        boxes.Box(BoxId)!.City.Should().Be("Coventry");
    }

    [Fact]
    public async Task An_ordinary_update_cannot_forge_a_validation()
    {
        // Weight and the validation record are not fields of the box body at all.
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/boxes/{BoxId}",
            new { city = "Dover", weightKg = 99, validatedByPersonId = Loader, validatedAt = "2026-01-01T00:00:00Z" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        boxes.Box(BoxId)!.Validated.Should().BeFalse();
        boxes.Box(BoxId)!.WeightKg.Should().Be(0);
        boxes.Box(BoxId)!.City.Should().Be("Dover");
    }

    [Fact]
    public async Task Packing_an_item_keeps_its_open_ended_properties()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        await client.PostAsJsonAsync($"/boxes/{BoxId}/items", AnItemBody(), TestContext.Current.CancellationToken);

        var items = await client.GetFromJsonAsync<JsonElement>(
            $"/boxes/{BoxId}/items", TestContext.Current.CancellationToken);

        var packed = items.EnumerateArray().Should().ContainSingle().Subject;
        packed.GetProperty("description").GetString().Should().Be("Blankets");
        packed.GetProperty("properties").GetProperty("size").GetString().Should().Be("double");
    }

    [Fact]
    public async Task An_item_with_no_description_is_a_validation_problem()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/items", new { description = "" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        boxes.Items(BoxId).Should().BeEmpty();
    }

    [Fact]
    public async Task The_contents_of_an_unpacked_box_are_an_empty_list_not_a_404()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var items = await client.GetFromJsonAsync<JsonElement>(
            $"/boxes/{BoxId}/items", TestContext.Current.CancellationToken);
        items.EnumerateArray().Should().BeEmpty();

        var missing = await client.GetAsync("/boxes/999/items", TestContext.Current.CancellationToken);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unpacking_an_item_from_a_box_it_was_never_in_is_a_404()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync(
            $"/boxes/{BoxId}/items/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
