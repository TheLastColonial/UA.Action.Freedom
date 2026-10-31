using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// ADR 0008 and O36: <c>GET /convoys/{id}/readiness</c> is the list of requirements, and <c>POST /convoys/{id}/depart</c>
/// is the one departure, refused with that list while a blocking requirement is outstanding. There is no override (O3).
/// </summary>
public class ConvoyDepartureEndpointTests
{
    private const int Id = 42;
    private const string Vin = "WVWZZZ1JZXW000001";
    private const string ManifestId = "MAN-0001";
    private static readonly Guid DriverId = new("0b7e8f2a-4c1d-4e5f-9a6b-7c8d9e0f1a2b");
    private static readonly Guid Hospital = new("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly DateTime Start = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    private static string Vehicle => $"/convoys/{Id}/vehicles/{Vin}";

    private static InMemoryConvoyRepository AConvoy() =>
        new InMemoryConvoyRepository(new ConvoyReadModel(Id, Start, Start.AddDays(4), Start.AddDays(-12)))
            .WithVehicle(Vin, onConvoy: Id)
            .WithPerson(DriverId, "Olena", "Bondar")
            .WithCrew(Vin, DriverId);

    private static InMemoryReceiverRepository Receivers() =>
        new(new ReceiverReadModel(Hospital, "Kharkiv Regional Hospital", "Kharkiv oblast", ReceiverStatus.Registered));

    private static InMemoryManifestRepository Manifests(ManifestStatus status = ManifestStatus.Ready) =>
        new(new ManifestReadModel(ManifestId, Id, Vin, status, null, Start.AddDays(-5)));

    private static HttpClient ClientFor(
        out InMemoryConvoyRepository convoys,
        out InMemoryManifestRepository manifests,
        out IAsyncDisposable api,
        string role = "Administrator",
        InMemoryConvoyRepository? existing = null)
    {
        convoys = existing ?? AConvoy();
        manifests = Manifests();
        var factory = FreedomApi.WithManifests(
            manifests, convoys, InMemoryPersonRepository.WithLinkedTestUser(), new RecordingManifestWorkQueue(),
            receivers: Receivers(), roles: role);
        api = factory;
        return factory.CreateClient();
    }

    private static StringContent AnEnsBody() => new(
        """{ "mrn": "25FR17551780961AT5", "acceptedAt": "2026-08-24T09:30:00+00:00", "filedBy": "groundofficer", "filingReference": "STP-1" }""",
        Encoding.UTF8,
        "application/json");

    /// <summary>Does through the API what a Dispatcher would, until nothing blocks.</summary>
    private static async Task MakeReadyAsync(HttpClient client)
    {
        var ct = TestContext.Current.CancellationToken;
        var steps = new[]
        {
            await client.PutAsJsonAsync($"/convoys/{Id}/route", new
            {
                stops = new object[]
                {
                    new { name = "Coventry depot", kind = "Hub", house = "Unit 4", street = "Cross Road", city = "Coventry", country = "United Kingdom", postcode = "CV1 2AB" },
                    new { name = "Warsaw hub", street = "Trasa Katowicka", city = "Warszawa", country = "Poland", postcode = "80-180" },
                },
            }, ct),
            await client.PutAsJsonAsync($"/convoys/{Id}/leader", new { personId = DriverId }, ct),
            await client.PutAsJsonAsync($"{Vehicle}/insurance", new
            {
                insurer = "Ukraine Aid Mutual", policyNumber = "POL-1", coverStart = "2020-01-01", coverEnd = "2099-01-01", costGbp = 412.50m,
            }, ct),
            await client.PutAsJsonAsync($"{Vehicle}/ferry", new
            {
                @operator = "P&O Ferries", reference = "POF-1", sailingAt = "2026-09-02T07:30:00Z", ticketDetails = "Freight",
            }, ct),
            await client.PutAsJsonAsync($"{Vehicle}/handover-receiver", new { receiverRef = Hospital }, ct),
            await client.PutAsync($"{Vehicle}/declarations/ens", AnEnsBody(), ct),
            await client.PostAsJsonAsync($"{Vehicle}/declarations/gmr/record", new { reference = "GMR-1" }, ct),
            await client.PostAsJsonAsync($"{Vehicle}/declarations/elo/record", new { reference = "ELO-1" }, ct),
        };

        steps.Select(step => step.StatusCode).Should().OnlyContain(
            code => (int)code >= 200 && (int)code < 300, "every setup step should succeed");
    }

    private static async Task<JsonElement> ReadinessAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>($"/convoys/{Id}/readiness", TestContext.Current.CancellationToken);

    private static string[] CodesOutstanding(JsonElement document, string property) =>
        [.. document.GetProperty(property).EnumerateArray()
            .Where(requirement => requirement.GetProperty("severity").GetString() == "Blocking"
                                  && requirement.GetProperty("state").GetString() != "Done")
            .Select(requirement => requirement.GetProperty("code").GetString()!)];

    [Fact]
    public async Task Readiness_reports_every_requirement_with_its_scope_owner_severity_and_link()
    {
        using var client = ClientFor(out _, out _, out var api);
        await using var host = api;

        var readiness = await ReadinessAsync(client);

        readiness.GetProperty("ready").GetBoolean().Should().BeFalse();
        var leader = readiness.GetProperty("requirements").EnumerateArray().Single(r => r.GetProperty("code").GetString() == "leader");
        leader.GetProperty("scope").GetString().Should().Be("Convoy");
        leader.GetProperty("state").GetString().Should().Be("ToDo");
        leader.GetProperty("ownerRole").GetString().Should().Be("Dispatcher");
        leader.GetProperty("severity").GetString().Should().Be("Blocking");
        leader.GetProperty("resolveLink").GetString().Should().Be($"/convoys/{Id}/leader");
        readiness.GetProperty("requirements").EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == "ferry").GetProperty("vin").GetString().Should().Be(Vin);
    }

    [Fact]
    public async Task Departure_is_refused_with_every_outstanding_blocking_requirement_and_stamps_nothing()
    {
        using var client = ClientFor(out var convoys, out var manifests, out var api);
        await using var host = api;

        var response = await client.PostAsync($"/convoys/{Id}/depart", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("type").GetString().Should().Be("convoy-not-ready");
        CodesOutstanding(problem, "requirements").Should().Contain(
            ["route", "leader", "insurance", "ferry", "handover-receiver", "declaration.gmr", "declaration.ens", "declaration.elo"]);
        convoys.Departed(Id).Should().BeFalse();
        manifests.Manifest(ManifestId)!.Status.Should().Be(ManifestStatus.Ready);
    }

    [Fact]
    public async Task What_the_readiness_read_says_is_exactly_what_departure_enforces()
    {
        using var client = ClientFor(out _, out _, out var api);
        await using var host = api;
        var readiness = await ReadinessAsync(client);

        var response = await client.PostAsync($"/convoys/{Id}/depart", content: null, TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        CodesOutstanding(problem, "requirements").Should().BeEquivalentTo(CodesOutstanding(readiness, "requirements"));
    }

    [Fact]
    public async Task A_convoy_with_every_requirement_met_departs_once_and_a_second_departure_is_refused()
    {
        using var client = ClientFor(out var convoys, out var manifests, out var api);
        await using var host = api;
        await MakeReadyAsync(client);

        var readiness = await ReadinessAsync(client);
        var first = await client.PostAsync($"/convoys/{Id}/depart", content: null, TestContext.Current.CancellationToken);
        var second = await client.PostAsync($"/convoys/{Id}/depart", content: null, TestContext.Current.CancellationToken);

        readiness.GetProperty("ready").GetBoolean().Should().BeTrue(string.Join(", ", CodesOutstanding(readiness, "requirements")));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("type").GetString().Should().Be("convoy-already-departed");
        convoys.Departed(Id).Should().BeTrue();
        manifests.Manifest(ManifestId)!.Status.Should().Be(ManifestStatus.InTransit);
    }

    [Fact]
    public async Task Taking_something_away_after_it_was_met_blocks_departure_again()
    {
        using var client = ClientFor(out _, out _, out var api);
        await using var host = api;
        await MakeReadyAsync(client);

        await client.DeleteAsync($"{Vehicle}/ferry", TestContext.Current.CancellationToken);
        var response = await client.PostAsync($"/convoys/{Id}/depart", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        CodesOutstanding(await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken), "requirements")
            .Should().Equal("ferry");
    }

    [Fact]
    public async Task A_departure_for_a_convoy_that_does_not_exist_is_a_404()
    {
        using var client = ClientFor(out _, out _, out var api);
        await using var host = api;

        var response = await client.PostAsync("/convoys/999/depart", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // No role can bypass it: the blocking requirements are outstanding for every caller, and only the roles that run a
    // convoy may even ask.
    [Theory]
    [InlineData("Administrator", HttpStatusCode.Conflict)]
    [InlineData("Dispatcher", HttpStatusCode.Conflict)]
    [InlineData("Loader", HttpStatusCode.Forbidden)]
    [InlineData("Purchaser", HttpStatusCode.Forbidden)]
    [InlineData("Mechanic", HttpStatusCode.Forbidden)]
    [InlineData("GroundOfficer", HttpStatusCode.Forbidden)]
    public async Task No_role_departs_a_convoy_that_is_not_ready(string role, HttpStatusCode expected)
    {
        using var client = ClientFor(out var convoys, out _, out var api, role);
        await using var host = api;

        var response = await client.PostAsync($"/convoys/{Id}/depart", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(expected);
        convoys.Departed(Id).Should().BeFalse();
    }

    [Fact]
    public async Task Departing_requires_a_token()
    {
        await using var api = FreedomApi.WithConvoys(AConvoy(), authenticated: false);
        using var client = api.CreateClient();

        var response = await client.PostAsync($"/convoys/{Id}/depart", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("override")]
    [InlineData("force")]
    [InlineData("depart-anyway")]
    public async Task There_is_no_route_that_overrides_the_requirements(string route)
    {
        using var client = ClientFor(out _, out _, out var api);
        await using var host = api;

        var response = await client.PostAsync($"/convoys/{Id}/{route}", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task A_withdrawn_vehicle_does_not_hold_the_convoy_back()
    {
        var convoys = AConvoy().WithWithdrawnVehicle("WVWZZZ1JZXW000009", Id);
        using var client = ClientFor(out _, out _, out var api, existing: convoys);
        await using var host = api;
        await MakeReadyAsync(client);

        var response = await client.PostAsync($"/convoys/{Id}/depart", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
