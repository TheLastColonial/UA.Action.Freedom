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

    /// <summary>The volunteer the test caller's login is linked to: whoever signs is whoever is calling.</summary>
    private static readonly Guid Loader = InMemoryPersonRepository.TestUserId;

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

    private static InMemoryPersonRepository AKnownLoader() => InMemoryPersonRepository.WithLinkedTestUser();

    private static object AnItemBody() => new
    {
        description = "Blankets",
        properties = new Dictionary<string, string> { ["size"] = "double" },
        categoryId = InMemoryItemCategoryRepository.OtherId,
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
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var validate = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/validate",
            new { weightKg = 24 },
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
            new { weightKg = 24 },
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
            new { weightKg = 24, widthCm = 40m, depthCm = 30m, heightCm = 20m },
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
            new { weightKg = 30 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        boxes.Box(BoxId)!.WeightKg.Should().Be(24);
    }

    [Fact]
    public async Task A_validator_named_in_the_body_is_ignored_the_caller_signs()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/validate",
            new { validatedByPersonId = Guid.NewGuid(), weightKg = 24 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var box = await client.GetFromJsonAsync<JsonElement>($"/boxes/{BoxId}", TestContext.Current.CancellationToken);
        box.GetProperty("validatedByPersonId").GetGuid().Should().Be(Loader);
    }

    [Fact]
    public async Task A_login_not_linked_to_a_volunteer_cannot_validate_a_box()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, new InMemoryPersonRepository(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/validate", new { weightKg = 24 }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("type").GetString().Should().Be("login-not-linked");
        boxes.Box(BoxId)!.Validated.Should().BeFalse();
    }

    [Fact]
    public async Task A_login_not_linked_to_a_volunteer_cannot_place_a_box_in_a_bay()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(
            boxes, new InMemoryPersonRepository(), new InMemoryBayRepository(AStoredBay()), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/boxes/{BoxId}/bay", new { bayId = BayId }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("type").GetString().Should().Be("login-not-linked");
        (await client.GetAsync($"/boxes/{BoxId}/bay", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "an unlinked Loader is scoped to nothing, so even the read is out of reach");
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
            new { weightKg = 9_999 },
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
    public async Task An_item_can_be_packed_under_a_donation_that_exists()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        var donations = new InMemoryDonationRepository().WithDonation(41, Guid.NewGuid(), new DateOnly(2026, 9, 20));
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), donations: donations, roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/items",
            new { description = "Tins", categoryId = InMemoryItemCategoryRepository.OtherId, donationId = 41 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        boxes.Items(BoxId).Should().ContainSingle().Which.DonationId.Should().Be(41);
    }

    [Fact]
    public async Task An_item_cannot_be_packed_under_a_donation_that_does_not_exist()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/items",
            new { description = "Tins", categoryId = InMemoryItemCategoryRepository.OtherId, donationId = 41 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("type").GetString().Should().Be("donation-not-found");
        boxes.Items(BoxId).Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_can_be_unpacked_from_a_validated_box()
    {
        var item = new BoxItemReadModel(
            Guid.NewGuid(), "Blankets", new Dictionary<string, string>(), InMemoryItemCategoryRepository.OtherId);
        var boxes = new InMemoryBoxRepository(ABox(validated: true)).WithItem(BoxId, item);
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync(
            $"/boxes/{BoxId}/items/{item.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        boxes.Items(BoxId).Should().ContainSingle();
    }

    [Fact]
    public async Task A_validated_box_cannot_be_deleted()
    {
        var boxes = new InMemoryBoxRepository(ABox(validated: true));
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/boxes/{BoxId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        boxes.Box(BoxId).Should().NotBeNull();
    }

    [Fact]
    public async Task A_box_nobody_has_validated_is_deleted()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/boxes/{BoxId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        boxes.Box(BoxId).Should().BeNull();
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
            new { bayId = BayId },
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
            new { bayId = BayId },
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
            new { bayId = BayId },
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
            new { bayId = BayId },
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

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Packing_an_item_returns_its_identifier_and_no_warnings_for_ordinary_goods()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/items", AnItemBody(), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("itemId").GetGuid().Should().Be(boxes.Items(BoxId).Single().Id);
        body.GetProperty("warnings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task An_item_keeps_its_category_quantity_value_expiry_and_code_and_reads_back_with_them()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();
        var expiresOn = Today.AddDays(400).ToString("yyyy-MM-dd");

        var added = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/items",
            new
            {
                description = "Paracetamol",
                categoryId = InMemoryItemCategoryRepository.MedicineId,
                quantity = 40,
                valueGbp = 62.5m,
                valueSource = "Estimate",
                expiresOn,
                commodityCode = "30049000",
            },
            TestContext.Current.CancellationToken);
        added.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await client.GetFromJsonAsync<JsonElement>(
            $"/boxes/{BoxId}/items", TestContext.Current.CancellationToken);

        var packed = items.EnumerateArray().Should().ContainSingle().Subject;
        packed.GetProperty("categoryId").GetInt32().Should().Be(InMemoryItemCategoryRepository.MedicineId);
        packed.GetProperty("categoryNameEn").GetString().Should().Be("Medicine");
        packed.GetProperty("quantity").GetInt32().Should().Be(40);
        packed.GetProperty("valueGbp").GetDecimal().Should().Be(62.5m);
        packed.GetProperty("valueSource").GetString().Should().Be("Estimate");
        packed.GetProperty("expiresOn").GetString().Should().Be(expiresOn);
        packed.GetProperty("commodityCode").GetString().Should().Be("30049000");
        packed.GetProperty("shelfLife").GetString().Should().Be("Fine");
        packed.GetProperty("isNotCarried").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task An_item_within_its_category_short_dated_window_reads_back_as_short()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/items",
            new
            {
                description = "Paracetamol",
                categoryId = InMemoryItemCategoryRepository.MedicineId,
                expiresOn = Today.AddDays(30).ToString("yyyy-MM-dd"),
            },
            TestContext.Current.CancellationToken);

        var items = await client.GetFromJsonAsync<JsonElement>(
            $"/boxes/{BoxId}/items", TestContext.Current.CancellationToken);

        items[0].GetProperty("shelfLife").GetString().Should().Be("Short");
    }

    [Fact]
    public async Task Packing_an_item_the_convoy_will_not_carry_is_accepted_with_a_warning()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/items",
            new { description = "Camping gas", categoryId = InMemoryItemCategoryRepository.GasId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).Should().Equal("NotCarried");
        boxes.Items(BoxId).Should().ContainSingle();
    }

    [Fact]
    public async Task Packing_an_item_that_has_already_expired_is_accepted_with_a_warning()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/items",
            new
            {
                description = "Paracetamol",
                categoryId = InMemoryItemCategoryRepository.MedicineId,
                expiresOn = Today.AddDays(-1).ToString("yyyy-MM-dd"),
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).Should().Equal("Expired");
    }

    [Fact]
    public async Task An_item_under_a_category_that_does_not_exist_is_unprocessable()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/items",
            new { description = "Mystery", categoryId = 999 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        boxes.Items(BoxId).Should().BeEmpty();
    }

    [Theory]
    [InlineData("{\"description\":\"Blankets\"}")]
    [InlineData("{\"description\":\"Blankets\",\"categoryId\":1,\"valueGbp\":10}")]
    [InlineData("{\"description\":\"Blankets\",\"categoryId\":1,\"valueSource\":\"Donor\"}")]
    [InlineData("{\"description\":\"Blankets\",\"categoryId\":1,\"valueGbp\":10,\"valueSource\":\"Purchased\"}")]
    [InlineData("{\"description\":\"Blankets\",\"categoryId\":1,\"valueGbp\":-1,\"valueSource\":\"Donor\"}")]
    [InlineData("{\"description\":\"Blankets\",\"categoryId\":1,\"quantity\":0}")]
    [InlineData("{\"description\":\"Blankets\",\"categoryId\":1,\"commodityCode\":\"1234\"}")]
    public async Task An_item_without_a_category_or_with_a_malformed_value_quantity_or_code_is_rejected(string body)
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsync(
            $"/boxes/{BoxId}/items",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        boxes.Items(BoxId).Should().BeEmpty();
    }

    [Fact]
    public async Task A_box_with_an_expired_item_cannot_be_validated_until_it_comes_out()
    {
        var expired = new BoxItemReadModel(
            Guid.NewGuid(), "Paracetamol", new Dictionary<string, string>(),
            InMemoryItemCategoryRepository.MedicineId, ExpiresOn: Today.AddDays(-1));
        var boxes = new InMemoryBoxRepository(ABox()).WithItem(BoxId, expired);
        await using var api = FreedomApi.WithBoxes(boxes, AKnownLoader(), roles: "Loader");
        using var client = api.CreateClient();

        var refused = await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/validate", new { weightKg = 24 }, TestContext.Current.CancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("type").GetString().Should().Be("box-has-expired-items");
        boxes.Box(BoxId)!.Validated.Should().BeFalse();

        (await client.DeleteAsync($"/boxes/{BoxId}/items/{expired.Id}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.PostAsJsonAsync(
            $"/boxes/{BoxId}/validate", new { weightKg = 24 }, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
