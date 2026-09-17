using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Locations;
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

    private const int LocationId = 3;

    private static readonly Guid Loader = new("2b9c1e40-7d8a-4c31-9f52-6a0b8d3e5c11");

    private static BoxReadModel ABox(bool validated = false) => new(
        BoxId,
        WeightKg: validated ? 24 : 0,
        WidthCm: validated ? 40 : null,
        DepthCm: validated ? 30 : null,
        HeightCm: validated ? 20 : null,
        ReceiverRef: null,
        LocationId: LocationId,
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
            new { locationId = LocationId },
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
    public async Task A_loader_validates_a_box_and_records_its_dimensions()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/validate",
            new { validatedByPersonId = Loader, weightKg = 24, widthCm = 40m, depthCm = 30m, heightCm = 20m },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var box = await client.GetFromJsonAsync<JsonElement>($"/boxes/{BoxId}", TestContext.Current.CancellationToken);
        box.GetProperty("widthCm").GetDecimal().Should().Be(40m);
        box.GetProperty("depthCm").GetDecimal().Should().Be(30m);
        box.GetProperty("heightCm").GetDecimal().Should().Be(20m);
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
    public async Task Nothing_in_a_validated_box_can_be_edited_either()
    {
        var item = new BoxItemReadModel(Guid.NewGuid(), "Blankets", new Dictionary<string, string>());
        var boxes = new InMemoryBoxRepository(ABox(validated: true)).WithItem(BoxId, item);
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/boxes/{BoxId}/items/{item.Id}",
            new { description = "Blankets (large)" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        boxes.Items(BoxId).Single().Description.Should().Be("Blankets");
    }

    [Fact]
    public async Task A_validated_box_cannot_be_pointed_at_another_receiver()
    {
        var boxes = new InMemoryBoxRepository(ABox(validated: true));
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/boxes/{BoxId}",
            new { receiverRef = Guid.NewGuid(), locationId = 9 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        boxes.Box(BoxId)!.LocationId.Should().Be(LocationId);
    }

    [Fact]
    public async Task An_ordinary_update_cannot_forge_a_validation()
    {
        // Weight and the validation record are not fields of the box body at all.
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        const int newLocationId = 9;

        var response = await client.PutAsJsonAsync(
            $"/boxes/{BoxId}",
            new { locationId = newLocationId, weightKg = 99, validatedByPersonId = Loader, validatedAt = "2026-01-01T00:00:00Z" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        boxes.Box(BoxId)!.Validated.Should().BeFalse();
        boxes.Box(BoxId)!.WeightKg.Should().Be(0);
        boxes.Box(BoxId)!.LocationId.Should().Be(newLocationId);
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

    [Fact]
    public async Task A_packed_items_description_and_properties_can_be_corrected()
    {
        var item = new BoxItemReadModel(Guid.NewGuid(), "Blankets", new Dictionary<string, string> { ["size"] = "double" });
        var boxes = new InMemoryBoxRepository(ABox()).WithItem(BoxId, item);
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/boxes/{BoxId}/items/{item.Id}",
            new { description = "Blankets (large)", properties = new Dictionary<string, string> { ["size"] = "XL" } },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var updated = boxes.Items(BoxId).Single();
        updated.Description.Should().Be("Blankets (large)");
        updated.Properties["size"].Should().Be("XL");
    }

    [Fact]
    public async Task Editing_an_item_that_does_not_exist_is_a_404()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/boxes/{BoxId}/items/{Guid.NewGuid()}",
            new { description = "Blankets" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Editing_an_item_in_an_unknown_box_is_a_404()
    {
        var boxes = new InMemoryBoxRepository();
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/boxes/999/items/{Guid.NewGuid()}",
            new { description = "Blankets" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_edited_item_with_no_description_is_a_validation_problem()
    {
        var item = new BoxItemReadModel(Guid.NewGuid(), "Blankets", new Dictionary<string, string>());
        var boxes = new InMemoryBoxRepository(ABox()).WithItem(BoxId, item);
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/boxes/{BoxId}/items/{item.Id}",
            new { description = "" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        boxes.Items(BoxId).Single().Description.Should().Be("Blankets");
    }

    private const int BayId = 9;

    private static BayReadModel AStoredBay(int locationId = LocationId) => new(BayId, locationId, "A1");

    [Fact]
    public async Task A_loader_places_a_box_in_a_bay()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(
            boxes, AKnownLoader(), new InMemoryBayRepository(AStoredBay()), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/boxes/{BoxId}/bay",
            new { bayId = BayId, assignedByPersonId = Loader },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var assignment = await client.GetFromJsonAsync<JsonElement>(
            $"/boxes/{BoxId}/bay", TestContext.Current.CancellationToken);
        assignment.GetProperty("bayId").GetInt32().Should().Be(BayId);
    }

    [Fact]
    public async Task An_administrator_cannot_place_a_box_in_a_bay()
    {
        // Bay allocation is Loader-only — narrower than boxes:write — because it is the on-site,
        // physical act of shelving a box, not coordination.
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(
            boxes, AKnownLoader(), new InMemoryBayRepository(AStoredBay()), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/boxes/{BoxId}/bay",
            new { bayId = BayId, assignedByPersonId = Loader },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_dispatcher_cannot_place_a_box_in_a_bay_either()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(
            boxes, AKnownLoader(), new InMemoryBayRepository(AStoredBay()), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/boxes/{BoxId}/bay",
            new { bayId = BayId, assignedByPersonId = Loader },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_box_cannot_be_placed_in_a_bay_at_a_different_location()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(
            boxes, AKnownLoader(), new InMemoryBayRepository(AStoredBay(locationId: 999)), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/boxes/{BoxId}/bay",
            new { bayId = BayId, assignedByPersonId = Loader },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_loader_vacates_a_boxs_bay()
    {
        var assignment = new BoxBayAssignmentReadModel(1, BoxId, BayId, Loader, DateTime.UtcNow, VacatedAt: null);
        var boxes = new InMemoryBoxRepository(ABox()).WithBayAssignment(assignment);
        await using var api = FreedomApi.WithBoxes(
            boxes, AKnownLoader(), new InMemoryBayRepository(AStoredBay()), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/boxes/{BoxId}/bay", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/boxes/{BoxId}/bay", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Vacating_a_box_with_no_active_bay_is_a_404()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(
            boxes, AKnownLoader(), new InMemoryBayRepository(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/boxes/{BoxId}/bay", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Bay_history_keeps_a_vacated_assignment()
    {
        var vacated = new BoxBayAssignmentReadModel(
            1, BoxId, BayId, Loader, DateTime.UtcNow.AddHours(-1), VacatedAt: DateTime.UtcNow);
        var boxes = new InMemoryBoxRepository(ABox()).WithBayAssignment(vacated);
        await using var api = FreedomApi.WithBoxes(
            boxes, AKnownLoader(), new InMemoryBayRepository(AStoredBay()), roles: "Loader");
        using var client = api.CreateClient();

        var history = await client.GetFromJsonAsync<JsonElement>(
            $"/boxes/{BoxId}/bay/history", TestContext.Current.CancellationToken);

        var entry = history.EnumerateArray().Should().ContainSingle().Subject;
        entry.GetProperty("bayId").GetInt32().Should().Be(BayId);
        entry.GetProperty("active").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Moving_a_box_to_a_different_location_vacates_its_bay()
    {
        // The box is shelved in a bay at its original location. Once it is recorded as being
        // somewhere else, that bay assignment no longer names anywhere the box actually is.
        var assignment = new BoxBayAssignmentReadModel(1, BoxId, BayId, Loader, DateTime.UtcNow, VacatedAt: null);
        var boxes = new InMemoryBoxRepository(ABox()).WithBayAssignment(assignment);
        await using var api = FreedomApi.WithBoxes(
            boxes, AKnownLoader(), new InMemoryBayRepository(AStoredBay()), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/boxes/{BoxId}",
            new { locationId = 999 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/boxes/{BoxId}/bay", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Creating_a_box_with_an_invalid_guid_returns_a_friendly_error()
    {
        var boxes = new InMemoryBoxRepository();
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/boxes",
            new { receiverRef = "12345", locationId = LocationId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        problem.GetProperty("status").GetInt32().Should().Be(400);
        problem.GetProperty("detail").GetString().Should().Contain("GUID");
    }
}
