using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// ADR 0016, X2, O20, O27: the Convoy Leader reports a convoy's progress point by point, a Dispatcher may do the same on
/// their behalf and is named, a crossing closes that border's declarations, and nothing the page fetches is cached.
/// </summary>
public class ConvoyProgressEndpointTests
{
    private const int Mine = 7;
    private const int Theirs = 8;
    private const string Vin = "WVWZZZ1JZXW000001";
    private const string OtherVin = "WVWZZZ1JZXW000002";
    private const string BrokenDownVin = "WVWZZZ1JZXW000003";
    private const int Hub = 1;
    private const int UkBorder = 3;
    private const int Overnight = 4;
    private const int UaBorder = 5;
    private const int BoxId = 3;
    private const int HubLocation = 10;

    private static readonly Guid Me = InMemoryPersonRepository.TestUserId;
    private static readonly Guid Taras = new("1c8f9a3b-5d2e-4f60-8b7c-8d9e0f1a2b3c");
    private static readonly Guid ReceiverRef = new("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    private static RouteStopReadModel Stop(
        int sequence, string name, RoutePointKind kind, CustomsAuthority? authority = null) =>
        new(sequence, "1", "Somewhere Road", "Anytown", "United Kingdom", "AB1 2CD", Name: name, Kind: kind, Authority: authority);

    private static async Task<InMemoryConvoyRepository> ConvoysAsync(bool departed = true, Guid? leader = null)
    {
        var convoys = new InMemoryConvoyRepository(
                new ConvoyReadModel(Mine, Departs, Departs.AddDays(4), Departs.AddDays(-5), DepartedAt: departed ? Departs : null),
                new ConvoyReadModel(Theirs, Departs, Departs.AddDays(4), Departs.AddDays(-5), DepartedAt: Departs))
            .WithVehicle(Vin, onConvoy: Mine)
            .WithVehicle(OtherVin, onConvoy: Theirs)
            .WithPerson(Me, "Test", "User")
            .WithPerson(Taras, "Taras", "Melnyk")
            .WithCrew(Vin, Me)
            .WithCrew(Vin, Taras)
            .WithCrew(OtherVin, Taras)
            .WithKnownBox(new ManifestBoxReadModel(BoxId, 12, true));

        await convoys.ReplaceRouteAsync(
            Mine,
            [
                Stop(1, "Coventry hub", RoutePointKind.Hub),
                Stop(2, "Dover", RoutePointKind.Stop),
                Stop(3, "Calais", RoutePointKind.Border, CustomsAuthority.UK),
                Stop(4, "Krakow stay", RoutePointKind.Overnight),
                Stop(5, "Shehyni", RoutePointKind.Border, CustomsAuthority.UA),
            ],
            TestContext.Current.CancellationToken);

        if (leader is { } person)
        {
            await convoys.NominateAsync(Mine, person, DateTime.UtcNow, TestContext.Current.CancellationToken);
        }

        convoys.WithWithdrawnVehicle(BrokenDownVin, Mine);
        return convoys;
    }

    private static string Reached(int point, int convoy = Mine) => $"/convoys/{convoy}/route/{point}/reached";

    private static string Crossed(int point, int convoy = Mine) => $"/convoys/{convoy}/route/{point}/crossed";

    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object? body = null) =>
        client.PostAsJsonAsync(path, body ?? new { }, TestContext.Current.CancellationToken);

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    private static async Task<string?> ProblemType(HttpResponseMessage response) =>
        (await Json(response)).GetProperty("type").GetString();

    private static async Task<JsonElement> ProgressOf(HttpClient client, int convoy = Mine)
    {
        var response = await client.GetAsync($"/convoys/{convoy}/progress", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await Json(response);
    }

    private static JsonElement PointOf(JsonElement progress, int pointId) =>
        progress.GetProperty("points").EnumerateArray().Single(point => point.GetProperty("routePointId").GetInt32() == pointId);

    // ---- the leader, on their own convoy --------------------------------------------------------------------

    [Fact]
    public async Task The_leader_marks_a_stop_reached_and_it_shows_on_the_progress_view()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Me));
        using var client = api.CreateClient();

        var response = await Post(client, Reached(Overnight));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var marks = PointOf(await ProgressOf(client), Overnight).GetProperty("marks").EnumerateArray().ToList();
        marks.Should().ContainSingle();
        marks[0].GetProperty("kind").GetString().Should().Be("Reached");
        marks[0].GetProperty("onBehalfOfLeader").GetBoolean().Should().BeFalse();
        marks[0].GetProperty("recordedByName").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_leader_marks_a_border_crossed()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Me));
        using var client = api.CreateClient();

        (await Post(client, Crossed(UkBorder))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        PointOf(await ProgressOf(client), UkBorder).GetProperty("marks")[0].GetProperty("kind").GetString().Should().Be("Crossed");
    }

    [Fact]
    public async Task Only_a_border_can_be_crossed()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Me));
        using var client = api.CreateClient();

        var response = await Post(client, Crossed(Overnight));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ProblemType(response)).Should().Be("crossing-needs-border");
    }

    [Fact]
    public async Task A_point_is_marked_once()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Me));
        using var client = api.CreateClient();
        await Post(client, Reached(Hub));

        var again = await Post(client, Reached(Hub));

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemType(again)).Should().Be("already-marked");
    }

    [Fact]
    public async Task A_mark_can_be_made_for_one_vehicle_where_vehicles_split()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Me));
        using var client = api.CreateClient();

        (await Post(client, Reached(Hub), new { vin = Vin })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        PointOf(await ProgressOf(client), Hub).GetProperty("marks")[0].GetProperty("vin").GetString().Should().Be(Vin);
    }

    [Fact]
    public async Task A_vehicle_that_has_left_the_convoy_cannot_be_marked()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Me));
        using var client = api.CreateClient();

        (await Post(client, Reached(Hub), new { vin = BrokenDownVin })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Nothing_is_marked_before_the_convoy_departs()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(departed: false, leader: Me));
        using var client = api.CreateClient();

        var response = await Post(client, Reached(Hub));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemType(response)).Should().Be("convoy-not-departed");
    }

    [Fact]
    public async Task A_point_that_is_not_on_the_route_is_not_found()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Me));
        using var client = api.CreateClient();

        (await Post(client, Reached(99))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- the Dispatcher, on the leader's behalf -------------------------------------------------------------

    [Fact]
    public async Task A_dispatcher_marks_a_crossing_on_the_leaders_behalf_and_is_named()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Taras), roles: "Dispatcher");
        using var client = api.CreateClient();

        (await Post(client, Crossed(UkBorder))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var mark = PointOf(await ProgressOf(client), UkBorder).GetProperty("marks")[0];
        mark.GetProperty("onBehalfOfLeader").GetBoolean().Should().BeTrue();
        mark.GetProperty("recordedByName").GetString().Should().Be("Test User");
    }

    [Fact]
    public async Task A_dispatcher_who_is_also_the_sitting_leader_is_not_marking_on_anyones_behalf()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Me), roles: "Dispatcher");
        using var client = api.CreateClient();
        await Post(client, Reached(Hub));

        PointOf(await ProgressOf(client), Hub).GetProperty("marks")[0].GetProperty("onBehalfOfLeader").GetBoolean().Should().BeFalse();
    }

    // ---- scope ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_leader_of_another_convoy_is_refused_every_one_of_these_routes()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Me));
        using var client = api.CreateClient();

        foreach (var response in new[]
                 {
                     await client.GetAsync($"/convoys/{Theirs}/progress", TestContext.Current.CancellationToken),
                     await Post(client, Reached(Hub, Theirs)),
                     await Post(client, Crossed(UkBorder, Theirs)),
                     await Post(client, $"/convoys/{Theirs}/costs", new { type = "Fuel", amountGbp = 40 }),
                     await Post(client, $"/convoys/{Theirs}/vehicles/{OtherVin}/delivered"),
                     await Post(client, $"/convoys/{Theirs}/boxes/{BoxId}/outcome", new { outcome = "Seized" }),
                     await client.GetAsync($"/convoys/{Theirs}/outcomes", TestContext.Current.CancellationToken),
                 })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ProblemType(response)).Should().Be("out-of-scope");
        }
    }

    [Fact]
    public async Task A_loader_cannot_report_progress()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(), roles: "Loader");
        using var client = api.CreateClient();

        (await Post(client, Reached(Hub))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_ground_officer_cannot_see_progress()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(), roles: "GroundOfficer");
        using var client = api.CreateClient();

        (await client.GetAsync($"/convoys/{Mine}/progress", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- fuel -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_leader_enters_fuel_on_their_own_convoy()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Me));
        using var client = api.CreateClient();

        (await Post(client, $"/convoys/{Mine}/costs", new { type = "Fuel", amountGbp = 82.5 }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_leader_enters_fuel_and_nothing_else()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Me));
        using var client = api.CreateClient();

        var response = await Post(client, $"/convoys/{Mine}/costs", new { type = "Other", amountGbp = 20 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_dispatcher_still_enters_other_costs()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(), roles: "Dispatcher");
        using var client = api.CreateClient();

        (await Post(client, $"/convoys/{Mine}/costs", new { type = "Other", amountGbp = 20 }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_leader_cannot_read_or_delete_the_budget()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Me));
        using var client = api.CreateClient();
        var token = TestContext.Current.CancellationToken;

        (await client.GetAsync($"/convoys/{Mine}/costs", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/convoys/{Mine}/budget", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync($"/convoys/{Mine}/costs/1", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- box outcomes and delivery from the road ------------------------------------------------------------

    private static async Task<WebApplicationFactory<Program>> LeaderWithABoxAsync()
    {
        var convoys = await ConvoysAsync(leader: Me);
        await convoys.AllocateBoxAsync(Mine, Vin, BoxId, TestContext.Current.CancellationToken);

        var boxes = new InMemoryBoxRepository(new BoxReadModel(BoxId, 12, null, null, null, ReceiverRef, null, null, null));
        var locations = new InMemoryLocationRepository(
            new LocationReadModel(HubLocation, "Coventry hub", null, null, null, null, null, IsRegisteredHub: true));

        return FreedomApi.WithManifests(
            new InMemoryManifestRepository(), convoys, new InMemoryPersonRepository().CalledByALinkedVolunteer(),
            new RecordingManifestWorkQueue(), boxes: boxes, locations: locations);
    }

    [Fact]
    public async Task The_leader_records_a_seized_box()
    {
        await using var api = await LeaderWithABoxAsync();
        using var client = api.CreateClient();

        var response = await Post(client, $"/convoys/{Mine}/boxes/{BoxId}/outcome", new { outcome = "Seized" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(await client.GetAsync($"/convoys/{Mine}/outcomes", TestContext.Current.CancellationToken)))[0]
            .GetProperty("outcome").GetString().Should().Be("Seized");
    }

    [Fact]
    public async Task The_leader_returns_a_box_to_a_registered_hub()
    {
        await using var api = await LeaderWithABoxAsync();
        using var client = api.CreateClient();

        var response = await Post(
            client, $"/convoys/{Mine}/boxes/{BoxId}/outcome", new { outcome = "ReturnedToHub", hubLocationId = HubLocation });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_leader_records_a_box_as_undeliverable_with_a_reason()
    {
        await using var api = await LeaderWithABoxAsync();
        using var client = api.CreateClient();

        (await Post(client, $"/convoys/{Mine}/boxes/{BoxId}/outcome", new { outcome = "Undeliverable" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Post(client, $"/convoys/{Mine}/boxes/{BoxId}/outcome", new { outcome = "Undeliverable", reason = "damaged" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_leader_marks_a_vehicle_delivered()
    {
        await using var api = await LeaderWithABoxAsync();
        using var client = api.CreateClient();

        (await Post(client, $"/convoys/{Mine}/vehicles/{Vin}/delivered")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_leader_cannot_close_the_convoy_or_accept_a_goods_list()
    {
        await using var api = await LeaderWithABoxAsync();
        using var client = api.CreateClient();

        (await Post(client, $"/convoys/{Mine}/close")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Post(client, $"/convoys/{Mine}/vehicles/{Vin}/declarations/goods-list/{ReceiverRef}/accepted"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- a crossing closes declarations ---------------------------------------------------------------------

    private static async Task<(WebApplicationFactory<Program> Api, InMemoryDeclarationRepository Declarations)> CrossingWorldAsync()
    {
        var convoys = await ConvoysAsync(leader: Me);
        var declarations = new InMemoryDeclarationRepository(convoys)
            .WithDeclaration(Mine, Vin, DeclarationKind.Gmr, DeclarationStatus.Accepted)
            .WithDeclaration(Mine, Vin, DeclarationKind.Ens, DeclarationStatus.Accepted)
            .WithDeclaration(Mine, Vin, DeclarationKind.Elo, DeclarationStatus.Accepted)
            .WithDeclaration(Mine, Vin, DeclarationKind.GoodsList, DeclarationStatus.Accepted, ReceiverRef)
            .WithDeclaration(Mine, BrokenDownVin, DeclarationKind.Gmr, DeclarationStatus.Accepted)
            .WithDeclaration(Theirs, OtherVin, DeclarationKind.Gmr, DeclarationStatus.Accepted);

        var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(), convoys, new InMemoryPersonRepository().CalledByALinkedVolunteer(),
            new RecordingManifestWorkQueue(), declarations: declarations);
        return (api, declarations);
    }

    private static DeclarationStatus StatusOf(InMemoryDeclarationRepository declarations, string vin, DeclarationKind kind) =>
        declarations.All.Single(row => row.Vin == vin && row.Kind == kind).Status;

    [Fact]
    public async Task Crossing_the_UK_border_closes_the_GMR_of_each_vehicle_crossing_and_nothing_else()
    {
        var (api, declarations) = await CrossingWorldAsync();
        await using var _ = api;
        using var client = api.CreateClient();

        (await Post(client, Crossed(UkBorder))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        StatusOf(declarations, Vin, DeclarationKind.Gmr).Should().Be(DeclarationStatus.Closed);
        StatusOf(declarations, Vin, DeclarationKind.Ens).Should().Be(DeclarationStatus.Accepted);
        StatusOf(declarations, Vin, DeclarationKind.Elo).Should().Be(DeclarationStatus.Accepted);
        StatusOf(declarations, Vin, DeclarationKind.GoodsList).Should().Be(DeclarationStatus.Accepted);
    }

    [Fact]
    public async Task A_vehicle_that_has_left_the_convoy_and_another_convoys_vehicle_keep_their_declarations()
    {
        var (api, declarations) = await CrossingWorldAsync();
        await using var _ = api;
        using var client = api.CreateClient();

        await Post(client, Crossed(UkBorder));

        StatusOf(declarations, BrokenDownVin, DeclarationKind.Gmr).Should().Be(DeclarationStatus.Accepted);
        StatusOf(declarations, OtherVin, DeclarationKind.Gmr).Should().Be(DeclarationStatus.Accepted);
    }

    [Fact]
    public async Task Crossing_into_Ukraine_closes_the_goods_lists()
    {
        var (api, declarations) = await CrossingWorldAsync();
        await using var _ = api;
        using var client = api.CreateClient();

        await Post(client, Crossed(UaBorder));

        StatusOf(declarations, Vin, DeclarationKind.GoodsList).Should().Be(DeclarationStatus.Closed);
        StatusOf(declarations, Vin, DeclarationKind.Gmr).Should().Be(DeclarationStatus.Accepted);
    }

    [Fact]
    public async Task A_crossing_for_one_vehicle_closes_only_that_vehicles_declarations()
    {
        var (api, declarations) = await CrossingWorldAsync();
        await using var _ = api;
        using var client = api.CreateClient();

        await Post(client, Crossed(UkBorder), new { vin = OtherVin });

        StatusOf(declarations, Vin, DeclarationKind.Gmr).Should().Be(DeclarationStatus.Accepted);
    }

    [Fact]
    public async Task Only_a_border_that_is_reached_leaves_declarations_open()
    {
        var (api, declarations) = await CrossingWorldAsync();
        await using var _ = api;
        using var client = api.CreateClient();

        await Post(client, Reached(UkBorder));

        StatusOf(declarations, Vin, DeclarationKind.Gmr).Should().Be(DeclarationStatus.Accepted);
    }

    // ---- a mark pins its route point ------------------------------------------------------------------------

    [Fact]
    public async Task A_route_point_with_a_mark_cannot_be_removed_from_the_route()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Me), roles: ["Dispatcher", "Administrator"]);
        using var client = api.CreateClient();
        await Post(client, Reached(Hub));
        var route = await Json(await client.GetAsync($"/convoys/{Mine}/route", TestContext.Current.CancellationToken));
        var withoutTheHub = route.EnumerateArray().Skip(1).ToList();

        var response = await client.PutAsJsonAsync($"/convoys/{Mine}/route", new { stops = withoutTheHub }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemType(response)).Should().Be("route-point-in-use");
    }

    // ---- what the page may be sent --------------------------------------------------------------------------

    private static readonly string[] AddressWords =
        ["house", "street", "city", "country", "countrycode", "postcode", "address", "latitude", "longitude", "position", "location"];

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => PropertyNames(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };

    [Fact]
    public async Task The_progress_view_carries_names_and_kinds_but_no_address_and_no_position()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Me));
        using var client = api.CreateClient();
        await Post(client, Reached(Hub));

        PropertyNames(await ProgressOf(client)).Select(name => name.ToLowerInvariant())
            .Should().NotContain(AddressWords);
    }

    [Fact]
    public async Task Every_answer_the_checklist_fetches_is_sent_no_store_even_a_refusal()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Me));
        using var client = api.CreateClient();
        var token = TestContext.Current.CancellationToken;

        var responses = new[]
        {
            await client.GetAsync($"/convoys/{Mine}/progress", token),
            await client.GetAsync($"/convoys/{Theirs}/progress", token),
            await Post(client, Reached(Hub)),
            await Post(client, Reached(Hub)),
            await Post(client, Crossed(Overnight)),
            await Post(client, $"/convoys/{Mine}/costs", new { type = "Other", amountGbp = 1 }),
            await client.GetAsync($"/convoys/{Mine}/outcomes", token),
            await client.GetAsync($"/convoys/{Mine}/vehicles/{Vin}/boxes", token),
        };

        responses.Select(r => r.StatusCode).Should().Contain([
            HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.Forbidden, HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity]);
        foreach (var response in responses)
        {
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
        }
    }

    [Fact]
    public async Task The_progress_view_gives_HQs_number_for_the_fallback_when_it_is_configured()
    {
        await using var api = FreedomApi.WithScope(await ConvoysAsync(leader: Me))
            .WithWebHostBuilder(builder => builder.UseSetting("App:HqPhone", "+44 20 7946 0000"));
        using var client = api.CreateClient();

        (await ProgressOf(client)).GetProperty("hqPhone").GetString().Should().Be("+44 20 7946 0000");
    }
}
