using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>
/// Steps for the Convoy Leader's progress reports (ADR 0016): the seed "leader" login made the leader of the scenario's
/// convoy, a border added to its route, and marks made and read back. The convoy and vehicle are the ones remembered by
/// <see cref="ManifestsSteps"/> and <see cref="OutcomesSteps"/>.
/// </summary>
[Binding]
public sealed class ProgressSteps(FreedomApiClient api, ScenarioState state)
{
    private const string ConvoyKey = "convoy-for-manifest";
    private const string VehicleKey = "vehicle-for-manifest";
    private const string OtherConvoyKey = "other-convoy-for-progress";

    private string ConvoyPath => $"/convoys/{state.Pinned(ConvoyKey)}";

    private string VehiclePath => $"{ConvoyPath}/vehicles/{state.Pinned(VehicleKey)}";

    private async Task<string> PersonOfAsync(string login)
    {
        var token = await api.TokenForAsync(login);
        var response = await api.SendAsync(HttpMethod.Get, "/me", token, null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the body was: {0}", api.LastBody);

        var personId = JsonDocument.Parse(api.LastBody).RootElement.GetProperty("personId");
        personId.ValueKind.Should().NotBe(JsonValueKind.Null, "the {0} login must be linked to a volunteer", login);
        return personId.GetString()!;
    }

    private async Task ExpectAsync(HttpMethod method, string path, string token, string? body, params HttpStatusCode[] expected)
    {
        var response = await api.SendAsync(method, path, token, body);

        expected.Should().Contain(response.StatusCode, "{0} {1} said: {2}", method, path, api.LastBody);
    }

    /// <summary>
    /// The seed "leader" login's volunteer joins the vehicle's crew and is nominated leader. Insurance names the drivers it
    /// covers, so it is recorded again once the crew has changed, as a Dispatcher would.
    /// </summary>
    [Given("the \"(.*)\" login's volunteer leads the convoy of the insured vehicle")]
    public async Task GivenTheLoginsVolunteerLeadsTheConvoy(string login)
    {
        var operatorToken = await api.TokenForAsync("operator");
        var person = await PersonOfAsync(login);
        var today = DateTime.UtcNow.Date;

        await ExpectAsync(HttpMethod.Put, $"{VehiclePath}/crew/{person}", operatorToken, "{}", HttpStatusCode.NoContent);
        await ExpectAsync(HttpMethod.Put, $"{VehiclePath}/insurance", operatorToken, $$"""
            { "insurer": "Ukraine Aid Mutual", "policyNumber": "BDD-2", "coverStart": "{{today.AddDays(-1):yyyy-MM-dd}}", "coverEnd": "{{today.AddDays(30):yyyy-MM-dd}}" }
            """, HttpStatusCode.NoContent);
        await ExpectAsync(
            HttpMethod.Put, $"{ConvoyPath}/leader", operatorToken, $$"""{ "personId": "{{person}}" }""", HttpStatusCode.OK);
    }

    /// <summary>Adds a Border point for an authority to the route, keeping the points that are there (an edit merges by id).</summary>
    [Given("the route of the convoy of the insured vehicle has a border into \"(.*)\"")]
    public async Task GivenTheRouteHasABorder(string authority)
    {
        var admin = await api.TokenForAsync("admin");
        await ExpectAsync(HttpMethod.Get, $"{ConvoyPath}/route", admin, null, HttpStatusCode.OK);

        var existing = JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Select(stop => $$"""{ "routePointId": {{stop.GetProperty("routePointId").GetInt32()}}, "name": {{JsonSerializer.Serialize(stop.GetProperty("name").GetString())}}, "kind": "{{stop.GetProperty("kind").GetString()}}"{{Authority(stop)}}, "city": {{JsonSerializer.Serialize(stop.GetProperty("city").GetString())}}, "country": {{JsonSerializer.Serialize(stop.GetProperty("country").GetString())}}, "postcode": {{JsonSerializer.Serialize(stop.GetProperty("postcode").GetString())}} }""")
            .ToList();
        existing.Add($$"""{ "name": "BDD border", "kind": "Border", "authority": "{{authority}}", "city": "Calais", "country": "France", "postcode": "62100" }""");

        await ExpectAsync(
            HttpMethod.Put, $"{ConvoyPath}/route", admin, $$"""{ "stops": [ {{string.Join(", ", existing)}} ] }""", HttpStatusCode.OK);
    }

    private static string Authority(JsonElement stop) =>
        stop.GetProperty("authority").GetString() is { } authority ? $$""", "authority": "{{authority}}" """ : string.Empty;

    [Given("another convoy exists")]
    public async Task GivenAnotherConvoyExists()
    {
        var operatorToken = await api.TokenForAsync("operator");
        var response = await api.SendAsync(HttpMethod.Post, "/convoys", operatorToken, """
            { "start": "2026-09-08T06:00:00Z", "expectedEnd": "2026-09-12T18:00:00Z" }
            """);
        response.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);

        var location = response.Headers.Location!;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.ToString();
        var id = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];
        state.CreatedResources.Add(("convoys", id));
        state.Pin(OtherConvoyKey, id);
    }

    private async Task<int> RoutePointIdAsync(string name)
    {
        var admin = await api.TokenForAsync("admin");
        await ExpectAsync(HttpMethod.Get, $"{ConvoyPath}/route", admin, null, HttpStatusCode.OK);

        return JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Single(stop => stop.GetProperty("name").GetString() == name)
            .GetProperty("routePointId").GetInt32();
    }

    private async Task MarkAsync(string name, string verb)
    {
        var token = state.CurrentToken;
        var pointId = await RoutePointIdAsync(name);
        await api.SendAsync(HttpMethod.Post, $"{ConvoyPath}/route/{pointId}/{verb}", token, "{}");
    }

    [When("I mark the route point \"(.*)\" of the convoy of the insured vehicle reached")]
    public Task WhenIMarkReached(string name) => MarkAsync(name, "reached");

    [When("I mark the route point \"(.*)\" of the convoy of the insured vehicle crossed")]
    public Task WhenIMarkCrossed(string name) => MarkAsync(name, "crossed");

    [When("I mark route point (\\d+) of the other convoy reached")]
    public Task WhenIMarkOtherConvoyPoint(int pointId) =>
        api.SendAsync(
            HttpMethod.Post, $"/convoys/{state.Pinned(OtherConvoyKey)}/route/{pointId}/reached", state.CurrentToken, "{}");

    [When("I add a \"(.*)\" cost of (\\d+) to the convoy of the insured vehicle")]
    public Task WhenIAddACost(string type, int amount) =>
        api.SendAsync(HttpMethod.Post, $"{ConvoyPath}/costs", state.CurrentToken, $$"""{ "type": "{{type}}", "amountGbp": {{amount}} }""");

    [Then("the response is sent no-store")]
    public void ThenTheResponseIsNoStore() =>
        api.LastResponse!.Headers.CacheControl?.NoStore.Should().BeTrue("the checklist must not be cached on the phone");

    private async Task<JsonElement> PointAsync(string name)
    {
        var response = await api.SendAsync(HttpMethod.Get, $"{ConvoyPath}/progress", state.CurrentToken, null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the body was: {0}", api.LastBody);

        return JsonDocument.Parse(api.LastBody).RootElement.GetProperty("points").EnumerateArray()
            .Single(point => point.GetProperty("name").GetString() == name).Clone();
    }

    [Then("the progress of the convoy of the insured vehicle shows \"(.*)\" as \"(.*)\" by the leader")]
    public async Task ThenTheProgressShowsByTheLeader(string name, string kind)
    {
        var marks = (await PointAsync(name)).GetProperty("marks").EnumerateArray().ToList();

        marks.Should().ContainSingle(mark => mark.GetProperty("kind").GetString() == kind);
        marks.Single().GetProperty("onBehalfOfLeader").GetBoolean().Should().BeFalse();
    }

    [Then("the progress of the convoy of the insured vehicle shows \"(.*)\" as \"(.*)\" on the leader's behalf")]
    public async Task ThenTheProgressShowsOnBehalf(string name, string kind)
    {
        var marks = (await PointAsync(name)).GetProperty("marks").EnumerateArray().ToList();

        marks.Should().ContainSingle(mark => mark.GetProperty("kind").GetString() == kind);
        marks.Single().GetProperty("onBehalfOfLeader").GetBoolean().Should().BeTrue();
        marks.Single().GetProperty("recordedByName").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
