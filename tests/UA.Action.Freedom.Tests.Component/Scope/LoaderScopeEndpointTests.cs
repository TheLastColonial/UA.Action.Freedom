using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Application.Manifests;

namespace UA.Action.Freedom.Tests.Component.Scope;

/// <summary>
/// A Loader sees only the locations they manage (O14, ADR 0010), through every route, lists and the label scan
/// included. Box 1 is at an assigned location, box 2 at another, box 3 is expected and not at any location yet.
/// </summary>
public class LoaderScopeEndpointTests
{
    private const int Assigned = 1;
    private const int Other = 2;
    private const int Mine = 1;
    private const int Theirs = 2;
    private const int Expected = 3;
    private const int ConvoyId = 7;
    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly Guid Me = InMemoryPersonRepository.TestUserId;

    private static BoxReadModel ABox(int id, int? locationId) =>
        new(id, 0, null, null, null, null, locationId, null, null);

    private static InMemoryBoxRepository Boxes() => new(
        ABox(Mine, Assigned), ABox(Theirs, Other), ABox(Expected, null));

    private static InMemoryLocationRepository Locations() => new(
        new LocationReadModel(Assigned, "Coventry", null, null, null, null, null),
        new LocationReadModel(Other, "Leeds", null, null, null, null, null));

    private static InMemoryLoaderAssignmentRepository ManagingOnlyTheFirst() =>
        new InMemoryLoaderAssignmentRepository().Managing(Me, Assigned);

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Api(
        string[] roles, InMemoryLoaderAssignmentRepository? loaders = null, InMemoryPersonRepository? people = null) =>
        FreedomApi.WithScope(
            new InMemoryConvoyRepository(new ConvoyReadModel(ConvoyId, DateTime.UtcNow, DateTime.UtcNow.AddDays(3), null))
                .WithVehicle(Vin, onConvoy: ConvoyId)
                .WithKnownBox(new ManifestBoxReadModel(Mine, 0, false))
                .WithKnownBox(new ManifestBoxReadModel(Theirs, 0, false)),
            Boxes(),
            Locations(),
            loaders ?? ManagingOnlyTheFirst(),
            people,
            roles);

    private static async Task<IReadOnlyList<int>> IdsAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<JsonElement>(path, TestContext.Current.CancellationToken))
        .EnumerateArray().Select(row => row.GetProperty("id").GetInt32()).ToList();

    private static HttpRequestMessage Request(HttpMethod method, string path, object? body = null) =>
        new(method, path) { Content = body is null ? null : JsonContent.Create(body) };

    private static readonly object ItemBody = new
    {
        description = "Blankets",
        properties = new Dictionary<string, string>(),
        categoryId = InMemoryItemCategoryRepository.OtherId,
    };

    /// <summary>Every route that names one box, by method, path and a valid body, so scope is what answers.</summary>
    public static TheoryData<string, string, bool> BoxRoutes() => new()
    {
        { "GET", "/boxes/{0}", true },
        { "GET", "/boxes/{0}/items", true },
        { "GET", "/boxes/{0}/qr-code", true },
        { "GET", "/boxes/{0}/qr-code/image", true },
        { "GET", "/boxes/{0}/label", true },
        { "GET", "/boxes/{0}/bay", true },
        { "GET", "/boxes/{0}/bay/history", true },
        { "DELETE", "/boxes/{0}", false },
        { "POST", "/boxes/{0}/items", false },
        { "DELETE", "/boxes/{0}/items/00000000-0000-0000-0000-000000000001", false },
        { "POST", "/boxes/{0}/validate", false },
        { "POST", "/boxes/{0}/qr-code", false },
        { "DELETE", "/boxes/{0}/qr-code", false },
        { "PUT", "/boxes/{0}/bay", false },
        { "DELETE", "/boxes/{0}/bay", false },
    };

    private static object? BodyFor(string method, string path) => (method, path) switch
    {
        ("POST", var p) when p.EndsWith("/items") => ItemBody,
        ("POST", var p) when p.EndsWith("/validate") => new { weightKg = 10 },
        ("PUT", var p) when p.EndsWith("/bay") => new { bayId = 99 },
        _ => null,
    };

    [Theory]
    [MemberData(nameof(BoxRoutes))]
    public async Task A_loader_is_refused_every_route_on_a_box_at_a_location_they_do_not_manage(
        string method, string template, bool isRead)
    {
        await using var api = Api(["Loader"]);
        using var client = api.CreateClient();
        var path = string.Format(template, Theirs);

        var response = await client.SendAsync(
            Request(new HttpMethod(method), path, BodyFor(method, path)), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {path} must be out of scope ({(isRead ? "read" : "write")})");
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("type").GetString().Should().Be("out-of-scope");
    }

    [Theory]
    [MemberData(nameof(BoxRoutes))]
    public async Task A_loader_is_not_refused_by_scope_on_a_box_at_a_location_they_manage(
        string method, string template, bool isRead)
    {
        await using var api = Api(["Loader"]);
        using var client = api.CreateClient();
        var path = string.Format(template, Mine);

        var response = await client.SendAsync(
            Request(new HttpMethod(method), path, BodyFor(method, path)), TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, $"{method} {path} is in scope ({(isRead ? "read" : "write")})");
    }

    [Theory]
    [MemberData(nameof(BoxRoutes))]
    public async Task An_unlocated_box_can_be_read_by_any_loader_but_written_by_none(
        string method, string template, bool isRead)
    {
        await using var api = Api(["Loader"]);
        using var client = api.CreateClient();
        var path = string.Format(template, Expected);

        var response = await client.SendAsync(
            Request(new HttpMethod(method), path, BodyFor(method, path)), TestContext.Current.CancellationToken);

        if (isRead)
        {
            response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task The_box_list_holds_only_assigned_locations_and_unlocated_boxes_for_a_loader()
    {
        await using var api = Api(["Loader"]);
        using var client = api.CreateClient();

        (await IdsAsync(client, "/boxes")).Should().Equal(Mine, Expected);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Dispatcher")]
    [InlineData("Purchaser")]
    public async Task Other_roles_see_every_box(string role)
    {
        await using var api = Api([role]);
        using var client = api.CreateClient();

        (await IdsAsync(client, "/boxes")).Should().Equal(Mine, Theirs, Expected);
        (await client.GetAsync($"/boxes/{Theirs}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_loader_with_no_assignment_sees_no_located_box_and_no_location()
    {
        await using var api = Api(["Loader"], new InMemoryLoaderAssignmentRepository());
        using var client = api.CreateClient();

        (await IdsAsync(client, "/boxes")).Should().Equal(Expected);
        (await IdsAsync(client, "/locations")).Should().BeEmpty();
        (await client.GetAsync($"/boxes/{Mine}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_loader_who_is_also_a_dispatcher_is_not_narrowed_because_the_roles_union()
    {
        await using var api = Api(["Loader", "Dispatcher"], new InMemoryLoaderAssignmentRepository());
        using var client = api.CreateClient();

        (await IdsAsync(client, "/boxes")).Should().Equal(Mine, Theirs, Expected);
    }

    [Fact]
    public async Task A_loader_whose_login_is_not_linked_reaches_nothing()
    {
        await using var api = Api(["Loader"], people: new InMemoryPersonRepository());
        using var client = api.CreateClient();

        (await client.GetAsync($"/boxes/{Mine}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IdsAsync(client, "/boxes")).Should().BeEmpty("an unresolved caller is shown nothing, not even an expected box");
    }

    [Fact]
    public async Task A_closed_assignment_no_longer_reaches_the_location()
    {
        var loaders = ManagingOnlyTheFirst();
        await loaders.UnassignAsync(Assigned, Me, DateTime.UtcNow, TestContext.Current.CancellationToken);
        await using var api = Api(["Loader"], loaders);
        using var client = api.CreateClient();

        (await client.GetAsync($"/boxes/{Mine}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Scanning_a_label_for_a_box_out_of_scope_answers_as_an_unknown_token_does()
    {
        var boxes = Boxes();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var token = TestContext.Current.CancellationToken;
        await boxes.IssueQrCodeAsync(Mine, mine, DateTime.UtcNow, token);
        await boxes.IssueQrCodeAsync(Theirs, theirs, DateTime.UtcNow, token);
        await using var api = FreedomApi.WithScope(null, boxes, Locations(), ManagingOnlyTheFirst(), null, "Loader");
        using var client = api.CreateClient();

        (await client.GetAsync($"/boxes/scan/{mine}", token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/boxes/scan/{theirs}", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/boxes/scan/{Guid.NewGuid()}", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_loader_creates_a_box_at_their_location_or_an_expected_one_but_not_elsewhere()
    {
        await using var api = Api(["Loader"]);
        using var client = api.CreateClient();
        var token = TestContext.Current.CancellationToken;

        (await client.PostAsJsonAsync("/boxes", new { locationId = Assigned }, token)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/boxes", new { }, token)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/boxes", new { locationId = Other }, token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_loader_moves_a_box_only_between_locations_they_manage_and_checks_an_expected_box_in()
    {
        var loaders = ManagingOnlyTheFirst().Managing(Me, 5);
        await using var api = Api(["Loader"], loaders);
        using var client = api.CreateClient();
        var token = TestContext.Current.CancellationToken;

        (await client.PutAsJsonAsync($"/boxes/{Mine}", new { locationId = 5 }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PutAsJsonAsync($"/boxes/{Mine}", new { locationId = Other }, token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync($"/boxes/{Mine}", new { }, token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync($"/boxes/{Expected}", new { locationId = Assigned }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_loader_reads_only_their_locations_and_their_bays()
    {
        await using var api = Api(["Loader"]);
        using var client = api.CreateClient();
        var token = TestContext.Current.CancellationToken;

        (await IdsAsync(client, "/locations")).Should().Equal(Assigned);
        (await client.GetAsync($"/locations/{Assigned}", token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/locations/{Assigned}/bays", token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/locations/{Other}", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/locations/{Other}/bays", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Putting_a_box_on_a_vehicle_needs_the_box_to_be_at_a_managed_location()
    {
        await using var api = Api(["Loader"]);
        using var client = api.CreateClient();
        var token = TestContext.Current.CancellationToken;

        (await client.PutAsync($"/convoys/{ConvoyId}/vehicles/{Vin}/boxes/{Theirs}", null, token))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync($"/convoys/{ConvoyId}/vehicles/{Vin}/boxes/{Theirs}", token))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsync($"/convoys/{ConvoyId}/vehicles/{Vin}/boxes/{Mine}", null, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public void Donor_and_donation_reads_have_nowhere_to_carry_a_box_id()
    {
        var properties = typeof(UA.Action.Freedom.Application.Donations.IDonationRepository).Assembly.GetTypes()
            .Where(type => type.Namespace == "UA.Action.Freedom.Application.Donations" && type.Name.EndsWith("ReadModel"))
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name);

        properties.Should().NotContain(name => name.Contains("Box"));
    }
}
