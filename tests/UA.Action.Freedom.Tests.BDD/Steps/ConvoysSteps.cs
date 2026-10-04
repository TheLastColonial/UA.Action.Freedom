using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>
/// Steps particular to <c>/convoys</c>. The generic HTTP and authentication steps live in
/// <see cref="ApiSteps"/>.
/// </summary>
[Binding]
public sealed class ConvoysSteps(FreedomApiClient api, ScenarioState state)
{
    [Then("the response body lists a route of (\\d+) stops")]
    public void ThenTheResponseBodyListsARouteOfStops(int count) =>
        JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray().Count()
            .Should().Be(count, "the body was: {0}", api.LastBody);

    [Then("route stop (\\d+) is in \"(.*)\"")]
    public void ThenRouteStopIsIn(int sequence, string city)
    {
        var stop = JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Single(element => element.GetProperty("sequence").GetInt32() == sequence);

        stop.GetProperty("city").GetString().Should().Be(city);
    }

    // "the response body lists a vehicle with VIN ..." is defined once, in VehiclesSteps.
    // Both /vehicles and /convoys/{id}/vehicles return an array of objects carrying a vin, so
    // the one definition serves both — and Reqnroll matches step text globally, so a second
    // copy here would be an ambiguous binding rather than an override.

    [Then("the response body lists no vehicles")]
    public void ThenTheResponseBodyListsNoVehicles() =>
        JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Should().BeEmpty("the body was: {0}", api.LastBody);

    /// <summary>
    /// Remembers the convoy just created, so a scenario can create a vehicle afterwards — which
    /// moves <c>{id}</c> on — and still address the convoy.
    /// </summary>
    [Given("I remember the convoy")]
    public void GivenIRememberTheConvoy() => state.Remember("convoy");

    [When("I PUT \"(.*)\" on the remembered convoy")]
    public Task WhenIPutOnTheRememberedConvoy(string template) =>
        api.SendAsync(HttpMethod.Put, state.Recall("convoy", template), state.CurrentToken, null);

    [When("I POST \"(.*)\" on the remembered convoy")]
    public Task WhenIPostOnTheRememberedConvoy(string template) =>
        api.SendAsync(HttpMethod.Post, state.Recall("convoy", template), state.CurrentToken, null);

    [When("I GET \"(.*)\" on the remembered convoy")]
    public Task WhenIGetOnTheRememberedConvoy(string template) =>
        api.SendAsync(HttpMethod.Get, state.Recall("convoy", template), state.CurrentToken, null);

    [When("I PUT \"(.*)\" on the remembered convoy with body:")]
    public Task WhenIPutOnTheRememberedConvoyWithBody(string template, string body) =>
        api.SendAsync(HttpMethod.Put, state.Recall("convoy", template), state.CurrentToken, body);

    [When("I DELETE \"(.*)\" on the remembered convoy")]
    public Task WhenIDeleteOnTheRememberedConvoy(string template) =>
        api.SendAsync(HttpMethod.Delete, state.Recall("convoy", template), state.CurrentToken, null);

    /// <summary>
    /// Adds a volunteer registered as a driver and pins their id as <c>{driver}</c>. Added as
    /// <c>admin</c> whatever the scenario's identity, because only the Administrator adds people.
    /// </summary>
    [Given("a driver exists")]
    public Task GivenADriverExists() => AddVolunteerAsync(DriverKey, isDriver: true);

    /// <summary>A volunteer who does not drive, pinned as <c>{passenger}</c>.</summary>
    [Given("a volunteer who does not drive exists")]
    public Task GivenAVolunteerWhoDoesNotDriveExists() => AddVolunteerAsync(PassengerKey, isDriver: false);

    [When("I PUT \"(.*)\" on the remembered convoy for the passenger with body:")]
    public Task WhenIPutOnTheRememberedConvoyForThePassengerWithBody(string template, string body) =>
        api.SendAsync(
            HttpMethod.Put,
            state.Recall("convoy", template).Replace("{passenger}", state.Pinned(PassengerKey), StringComparison.Ordinal),
            state.CurrentToken,
            body);

    /// <summary>
    /// A withdrawn vehicle stays on the truck list — the list is the record of what set off, and
    /// the manifest describing what it was carrying still has an entry to belong to.
    /// </summary>
    [Then("the vehicle \"(.*)\" is withdrawn from the truck list")]
    public void ThenTheVehicleIsWithdrawnFromTheTruckList(string vin) =>
        JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Single(vehicle => vehicle.GetProperty("vin").GetString() == vin)
            .GetProperty("withdrawn").GetBoolean().Should().BeTrue("the body was: {0}", api.LastBody);

    [Then("the crew lists the passenger as a {string}")]
    public void ThenTheCrewListsThePassengerAs(string role) =>
        JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Single(member => member.GetProperty("personId").GetString() == state.Pinned(PassengerKey))
            .GetProperty("role").GetString().Should().Be(role);

    private async Task AddVolunteerAsync(string key, bool isDriver)
    {
        var admin = await api.TokenForAsync("admin");
        var body = $$"""
            { "firstName": "Olena", "lastName": "Bondar", "dateOfBirth": "1985-01-01T00:00:00Z", "joined": "2024-01-01T00:00:00Z", "isDriver": {{(isDriver ? "true" : "false")}}, "committed": {{(isDriver ? "true" : "false")}} }
            """;

        var response = await api.SendAsync(HttpMethod.Post, "/people", admin, body);

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);
        var location = response.Headers.Location!;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.ToString();
        var personId = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];

        state.CreatedResources.Add(("people", personId));
        state.Pin(key, personId);
    }

    /// <summary>Crews the driver. The role is optional and defaults to Driver.</summary>
    [When("I PUT \"(.*)\" on the remembered convoy for the driver")]
    public Task WhenIPutOnTheRememberedConvoyForTheDriver(string template) =>
        api.SendAsync(HttpMethod.Put, ForTheDriver(template), state.CurrentToken, "{}");

    [When("I PUT \"(.*)\" on the remembered convoy for the driver with body:")]
    public Task WhenIPutOnTheRememberedConvoyForTheDriverWithBody(string template, string body) =>
        api.SendAsync(HttpMethod.Put, ForTheDriver(template), state.CurrentToken, body);

    [When("I DELETE \"(.*)\" on the remembered convoy for the driver")]
    public Task WhenIDeleteOnTheRememberedConvoyForTheDriver(string template) =>
        api.SendAsync(HttpMethod.Delete, ForTheDriver(template), state.CurrentToken, null);

    [Then("the response body lists the driver")]
    public void ThenTheResponseBodyListsTheDriver() =>
        JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Select(member => member.GetProperty("personId").GetString())
            .Should().Contain(state.Pinned(DriverKey), "the body was: {0}", api.LastBody);

    private const string RoutePointIdsKey = "routePointIds";

    /// <summary>
    /// Edits the route just read back by sending the same points in the opposite order, each with the id it was
    /// given, and remembers those ids. A point that kept its id is the same point, wherever it now sits.
    /// </summary>
    [When("I reverse the route of the remembered convoy keeping the point ids")]
    public async Task WhenIReverseTheRouteKeepingThePointIds()
    {
        var points = JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray().Reverse().ToList();
        state.Pin(RoutePointIdsKey, string.Join(",", points.Select(point => point.GetProperty("routePointId").GetInt32())));

        var stops = points.Select(point => new Dictionary<string, object?>
        {
            ["routePointId"] = point.GetProperty("routePointId").GetInt32(),
            ["name"] = point.GetProperty("name").GetString(),
            ["kind"] = point.GetProperty("kind").GetString(),
            ["authority"] = point.GetProperty("authority").GetString(),
            ["city"] = point.GetProperty("city").GetString(),
            ["postcode"] = point.GetProperty("postcode").GetString(),
        });

        await api.SendAsync(
            HttpMethod.Put,
            state.Recall("convoy", "/convoys/{id}/route"),
            state.CurrentToken,
            JsonSerializer.Serialize(new { stops }));
    }

    [Then("the route points keep the ids they had, in the new order")]
    public void ThenTheRoutePointsKeepTheirIds() =>
        string.Join(",", JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
                .Select(point => point.GetProperty("routePointId").GetInt32()))
            .Should().Be(state.Pinned(RoutePointIdsKey), "the body was: {0}", api.LastBody);

    [When("I nominate the driver as the leader of the remembered convoy")]
    public Task WhenINominateTheDriverAsLeader() =>
        api.SendAsync(
            HttpMethod.Put,
            state.Recall("convoy", "/convoys/{id}/leader"),
            state.CurrentToken,
            $$"""{ "personId": "{{state.Pinned(DriverKey)}}" }""");

    [Then("the convoy leader is the driver")]
    public void ThenTheConvoyLeaderIsTheDriver() =>
        JsonDocument.Parse(api.LastBody).RootElement.GetProperty("current").GetProperty("personId").GetString()
            .Should().Be(state.Pinned(DriverKey), "the body was: {0}", api.LastBody);

    [Then("the insurance does not yet cover the driver")]
    public void ThenTheInsuranceDoesNotYetCoverTheDriver() =>
        JsonDocument.Parse(api.LastBody).RootElement.GetProperty("uncoveredDrivers").EnumerateArray()
            .Select(id => id.GetString())
            .Should().Equal([state.Pinned(DriverKey)], "the body was: {0}", api.LastBody);

    [Then("the insurance covers every driver")]
    public void ThenTheInsuranceCoversEveryDriver() =>
        JsonDocument.Parse(api.LastBody).RootElement.GetProperty("uncoveredDrivers").GetArrayLength()
            .Should().Be(0, "the body was: {0}", api.LastBody);

    private const string DriverKey = "driver";
    private const string PassengerKey = "passenger";

    private string ForTheDriver(string template) =>
        state.Recall("convoy", template).Replace("{driver}", state.Pinned(DriverKey), StringComparison.Ordinal);
}
