using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component.Scope;

/// <summary>
/// A Convoy Leader reaches their own convoy and no other, only while they lead it (X12, ADR 0010). The role is derived
/// from the assignment, so these callers carry no role in their token at all.
/// </summary>
public class LeaderScopeEndpointTests
{
    private const int Mine = 7;
    private const int Theirs = 8;
    private const string Vin = "WVWZZZ1JZXW000001";
    private const string OtherVin = "WVWZZZ1JZXW000002";

    private static readonly Guid Me = InMemoryPersonRepository.TestUserId;
    private static readonly Guid Taras = new("1c8f9a3b-5d2e-4f60-8b7c-8d9e0f1a2b3c");

    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    private static InMemoryConvoyRepository TwoConvoys() =>
        new InMemoryConvoyRepository(
                new ConvoyReadModel(Mine, Departs, Departs.AddDays(4), null),
                new ConvoyReadModel(Theirs, Departs, Departs.AddDays(4), null))
            .WithVehicle(Vin, onConvoy: Mine)
            .WithVehicle(OtherVin, onConvoy: Theirs)
            .WithPerson(Me, "Test", "User")
            .WithPerson(Taras, "Taras", "Melnyk")
            .WithCrew(Vin, Me)
            .WithCrew(OtherVin, Taras)
            .WithKnownBox(new ManifestBoxReadModel(3, 12, true));

    private static async Task<InMemoryConvoyRepository> LeadingAsync(int convoyId, Guid person)
    {
        var convoys = TwoConvoys();
        await convoys.NominateAsync(convoyId, person, DateTime.UtcNow, TestContext.Current.CancellationToken);
        return convoys;
    }

    private static string[] Readable(int convoyId, string vin) =>
    [
        $"/convoys/{convoyId}",
        $"/convoys/{convoyId}/route",
        $"/convoys/{convoyId}/vehicles",
        $"/convoys/{convoyId}/vehicles/{vin}/boxes",
    ];

    [Fact]
    public async Task A_leader_reads_their_own_convoy_without_holding_any_role_in_their_token()
    {
        await using var api = FreedomApi.WithScope(await LeadingAsync(Mine, Me));
        using var client = api.CreateClient();

        foreach (var path in Readable(Mine, Vin))
        {
            (await client.GetAsync(path, TestContext.Current.CancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.OK, path);
        }
    }

    [Fact]
    public async Task A_leader_is_refused_every_one_of_those_routes_on_another_convoy()
    {
        await using var api = FreedomApi.WithScope(await LeadingAsync(Mine, Me));
        using var client = api.CreateClient();

        foreach (var path in Readable(Theirs, OtherVin))
        {
            var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
            (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
                .GetProperty("type").GetString().Should().Be("out-of-scope");
        }
    }

    [Fact]
    public async Task A_leader_is_refused_the_convoy_list_and_everything_else_a_dispatcher_reads()
    {
        await using var api = FreedomApi.WithScope(await LeadingAsync(Mine, Me));
        using var client = api.CreateClient();
        var token = TestContext.Current.CancellationToken;

        foreach (var path in new[]
                 {
                     "/convoys", $"/convoys/{Mine}/leader", $"/convoys/{Mine}/readiness", $"/convoys/{Mine}/tasks",
                     $"/convoys/{Mine}/vehicles/{Vin}/crew", $"/convoys/{Mine}/vehicles/{Vin}/declarations",
                     "/people", "/boxes", "/locations", "/receivers", "/vehicles",
                 })
        {
            (await client.GetAsync(path, token)).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
        }
    }

    [Fact]
    public async Task Reassigning_the_leader_takes_the_access_away_on_the_next_request()
    {
        var convoys = await LeadingAsync(Mine, Me);
        await using var api = FreedomApi.WithScope(convoys);
        using var client = api.CreateClient();
        var token = TestContext.Current.CancellationToken;
        (await client.GetAsync($"/convoys/{Mine}", token)).StatusCode.Should().Be(HttpStatusCode.OK);

        await convoys.WithCrew(Vin, Taras).NominateAsync(Mine, Taras, DateTime.UtcNow, token);

        (await client.GetAsync($"/convoys/{Mine}", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_role_of_that_name_in_the_token_grants_nothing_without_an_assignment()
    {
        await using var api = FreedomApi.WithScope(TwoConvoys(), roles: "ConvoyLeader");
        using var client = api.CreateClient();

        (await client.GetAsync($"/convoys/{Mine}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_login_not_linked_to_a_volunteer_is_never_a_leader()
    {
        await using var api = FreedomApi.WithScope(await LeadingAsync(Mine, Me), people: new InMemoryPersonRepository());
        using var client = api.CreateClient();

        (await client.GetAsync($"/convoys/{Mine}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_ground_officer_who_is_a_leader_keeps_the_isolation_of_that_role()
    {
        await using var api = FreedomApi.WithScope(await LeadingAsync(Mine, Me), roles: "GroundOfficer");
        using var client = api.CreateClient();

        (await client.GetAsync($"/convoys/{Mine}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Dispatcher")]
    [InlineData("Purchaser")]
    [InlineData("Loader")]
    public async Task The_roles_that_read_every_convoy_still_do(string role)
    {
        await using var api = FreedomApi.WithScope(TwoConvoys(), roles: role);
        using var client = api.CreateClient();

        foreach (var path in Readable(Theirs, OtherVin))
        {
            (await client.GetAsync(path, TestContext.Current.CancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.OK, path);
        }
    }

    [Fact]
    public async Task Me_reports_the_convoys_a_caller_leads_and_the_derived_role()
    {
        await using var api = FreedomApi.WithScope(await LeadingAsync(Mine, Me));
        using var client = api.CreateClient();

        var me = await client.GetFromJsonAsync<JsonElement>("/me", TestContext.Current.CancellationToken);

        me.GetProperty("ledConvoyIds").EnumerateArray().Select(id => id.GetInt32()).Should().Equal(Mine);
        me.GetProperty("roles").EnumerateArray().Select(role => role.GetString()).Should().Contain("ConvoyLeader");
    }
}
