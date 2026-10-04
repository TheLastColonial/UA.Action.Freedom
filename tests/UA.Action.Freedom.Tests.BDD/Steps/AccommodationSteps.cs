using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>Steps particular to a convoy's accommodation.</summary>
[Binding]
public sealed class AccommodationSteps(FreedomApiClient api, ScenarioState state)
{
    private const string RoutePointKey = "routePoint";
    private const string BookingKey = "booking";

    /// <summary>
    /// A convoy whose route has one overnight stop, with a vehicle crewed by a driver and a passenger. The
    /// convoy is remembered, the stop's id pinned as the overnight stop, and the people pinned as the driver
    /// and the passenger.
    /// </summary>
    [Given("a convoy with an overnight stop \"(.*)\" crews a driver and a passenger")]
    public async Task GivenAConvoyWithAnOvernightStopCrewsADriverAndAPassenger(string stopName)
    {
        var token = state.CurrentToken ?? await api.TokenForAsync("operator");

        await Ok(HttpMethod.Post, "/convoys", token, """{ "start": "2026-09-01T06:00:00Z", "expectedEnd": "2026-09-05T18:00:00Z" }""", HttpStatusCode.Created);
        state.Remember("convoy");
        var convoy = state.Pinned("convoy");
        state.CreatedResources.Add(("convoys", convoy));
        var vin = "BDDA" + Guid.NewGuid().ToString("N")[..13].ToUpperInvariant();
        state.Pin("vin", vin);

        var route = await Ok(
            HttpMethod.Put,
            $"/convoys/{convoy}/route",
            token,
            $$"""{ "stops": [ { "name": "{{stopName}}", "kind": "Overnight", "city": "{{stopName}}", "country": "France", "countryCode": "FR", "postcode": "59000" } ] }""",
            HttpStatusCode.OK);
        state.Pin(RoutePointKey, JsonDocument.Parse(route).RootElement[0].GetProperty("routePointId").GetInt32().ToString());

        await Ok(
            HttpMethod.Post,
            "/vehicles",
            token,
            $$"""{ "vin": "{{vin}}", "plate": "UA10ACT", "year": 2014, "fuel": "Diesel", "transmission": "Manual", "weightKg": 2200 }""",
            HttpStatusCode.Created);
        state.CreatedResources.Add(("vehicles", vin));
        await Ok(HttpMethod.Put, $"/vehicles/{vin}/inspection", token, """{ "status": "Passed" }""", HttpStatusCode.NoContent);
        await Ok(HttpMethod.Put, $"/convoys/{convoy}/vehicles/{vin}", token, null, HttpStatusCode.NoContent);

        var admin = await api.TokenForAsync("admin");
        var driver = await AddVolunteerAsync(admin, "Olena", isDriver: true);
        var passenger = await AddVolunteerAsync(admin, "Mykola", isDriver: false);
        state.Pin("driver", driver);
        state.Pin("passenger", passenger);

        await Ok(HttpMethod.Put, $"/convoys/{convoy}/vehicles/{vin}/crew/{driver}", token, "{}", HttpStatusCode.NoContent);
        await Ok(HttpMethod.Put, $"/convoys/{convoy}/vehicles/{vin}/crew/{passenger}", token, """{ "role": "Passenger" }""", HttpStatusCode.NoContent);
    }

    [When("I book \"(.*)\" at the overnight stop for the driver and the passenger")]
    public Task WhenIBookForTheDriverAndThePassenger(string provider) =>
        BookAsync(provider, state.Pinned("driver"), state.Pinned("passenger"));

    [When("I book \"(.*)\" at the overnight stop for the passenger")]
    public Task WhenIBookForThePassenger(string provider) => BookAsync(provider, state.Pinned("passenger"));

    [When("I flag the driver as arranging their own stay at the overnight stop")]
    public Task WhenIFlagTheDriver() =>
        api.SendAsync(
            HttpMethod.Put,
            $"/convoys/{state.Pinned("convoy")}/accommodation/self/{state.Pinned(RoutePointKey)}/{state.Pinned("driver")}",
            state.CurrentToken,
            null);

    [When("I DELETE \"(.*)\" on the remembered convoy for the passenger")]
    public Task WhenIDeleteForThePassenger(string template) =>
        api.SendAsync(
            HttpMethod.Delete,
            state.Recall("convoy", template)
                .Replace("{passenger}", state.Pinned("passenger"), StringComparison.Ordinal)
                .Replace("{vin}", state.Pinned("vin"), StringComparison.Ordinal),
            state.CurrentToken,
            null);

    [When("I migrate the booking from the passenger to the driver")]
    public Task WhenIMigrateTheBooking() =>
        api.SendAsync(
            HttpMethod.Post,
            $"/convoys/{state.Pinned("convoy")}/accommodation/{state.Pinned(BookingKey)}/migrate",
            state.CurrentToken,
            $$"""{ "fromPersonId": "{{state.Pinned("passenger")}}", "toPersonId": "{{state.Pinned("driver")}}" }""");

    [Then(@"the coverage has (\d+) nights? missing")]
    public void ThenTheCoverageHasNightsMissing(int expected) =>
        JsonDocument.Parse(api.LastBody).RootElement.GetProperty("missingCount").GetInt32()
            .Should().Be(expected, "the body was: {0}", api.LastBody);

    [Then("the tasks hold a leftover booking")]
    public void ThenTheTasksHoldALeftoverBooking() =>
        LeftoverTasks().Should().ContainSingle("the body was: {0}", api.LastBody);

    [Then("the tasks hold no leftover booking")]
    public void ThenTheTasksHoldNoLeftoverBooking() =>
        LeftoverTasks().Should().BeEmpty("the body was: {0}", api.LastBody);

    private IEnumerable<JsonElement> LeftoverTasks() =>
        JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Where(task => task.GetProperty("type").GetString() == "accommodation-leftover")
            .ToList();

    private async Task BookAsync(string provider, params string[] guests)
    {
        var response = await api.SendAsync(
            HttpMethod.Post,
            $"/convoys/{state.Pinned("convoy")}/accommodation",
            state.CurrentToken,
            JsonSerializer.Serialize(new
            {
                routePointId = int.Parse(state.Pinned(RoutePointKey)),
                provider,
                checkIn = "2026-09-02",
                checkOut = "2026-09-03",
                costGbp = 120,
                guests,
            }));

        if (response.StatusCode == HttpStatusCode.Created)
        {
            state.Pin(BookingKey, response.Headers.Location!.ToString().Split('/')[^1]);
        }
    }

    private async Task<string> Ok(HttpMethod method, string path, string token, string? body, HttpStatusCode expected)
    {
        var response = await api.SendAsync(method, path, token, body);
        response.StatusCode.Should().Be(expected, "{0} {1}: the body was: {2}", method, path, api.LastBody);
        if (response.StatusCode == HttpStatusCode.Created && response.Headers.Location is { } location)
        {
            state.LastCreatedKey = location.ToString().Split('/')[^1];
        }

        return api.LastBody;
    }

    private async Task<string> AddVolunteerAsync(string adminToken, string firstName, bool isDriver)
    {
        var body = $$"""
            { "firstName": "{{firstName}}", "lastName": "Bondar", "dateOfBirth": "1985-01-01T00:00:00Z", "joined": "2024-01-01T00:00:00Z", "isDriver": {{(isDriver ? "true" : "false")}}, "committed": {{(isDriver ? "true" : "false")}} }
            """;

        await Ok(HttpMethod.Post, "/people", adminToken, body, HttpStatusCode.Created);
        var personId = state.LastCreatedKey!;
        state.CreatedResources.Add(("people", personId));
        return personId;
    }
}
