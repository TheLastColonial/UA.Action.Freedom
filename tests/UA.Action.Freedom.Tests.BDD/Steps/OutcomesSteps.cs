using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>
/// Steps for how a load ends on a convoy (ADR 0015): a vehicle delivered, a box delivered, accepted, seized or returned to
/// a hub, a goods list accepted, and a convoy closed with its report. The convoy, the vehicle and the box are the ones
/// the scenario already remembered through <see cref="ManifestsSteps"/>, <see cref="DepartureSteps"/> and
/// <see cref="BoxesSteps"/>.
/// </summary>
[Binding]
public sealed class OutcomesSteps(FreedomApiClient api, ScenarioState state, DepartureSteps departure)
{
    private const string ConvoyKey = "convoy-for-manifest";
    private const string VehicleKey = "vehicle-for-manifest";
    private const string HubKey = "registered-hub";

    private string ConvoyPath => $"/convoys/{state.Pinned(ConvoyKey)}";

    private string VehiclePath => $"{ConvoyPath}/vehicles/{state.Pinned(VehicleKey)}";

    /// <summary>
    /// Everything a load needs before it can end: a validated box for the registered Receiver on the insured vehicle, the
    /// manifest signed off and readied, and the convoy departed. Done through the API as the people who would do it, so the
    /// scenarios that follow are about how the load ends and not about how it got on the road.
    /// </summary>
    [Given("the insured vehicle has set off carrying a validated box for the registered receiver")]
    public async Task GivenTheInsuredVehicleHasSetOffCarryingABox()
    {
        var operatorToken = await api.TokenForAsync("operator");
        var admin = await api.TokenForAsync("admin");

        var created = await api.SendAsync(
            HttpMethod.Post, "/boxes", operatorToken,
            $$"""{ "receiverRef": "{{state.Pinned(DepartureSteps.ReceiverKey)}}" }""");
        created.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);
        var location = created.Headers.Location!;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.ToString();
        var boxId = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];
        state.CreatedResources.Add(("boxes", boxId));
        state.Pin("box", boxId);

        await ExpectAsync(HttpMethod.Post, $"/boxes/{boxId}/items", operatorToken,
            $$"""{ "description": "Blankets", "categoryId": {{state.Pinned(CategoriesSteps.CategoryKey)}}, "properties": {} }""",
            HttpStatusCode.OK);
        await ExpectAsync(HttpMethod.Post, $"/boxes/{boxId}/validate", operatorToken, """{ "weightKg": 12 }""", HttpStatusCode.NoContent);
        await ExpectAsync(HttpMethod.Put, $"{VehiclePath}/boxes/{boxId}", operatorToken, null, HttpStatusCode.NoContent);

        var manifest = state.Pinned("manifest");
        await ExpectAsync(HttpMethod.Post, $"{VehiclePath}/manifest", operatorToken, $$"""{ "id": "{{manifest}}" }""", HttpStatusCode.Created);
        state.CreatedResources.Add(("manifests", manifest));
        await ExpectAsync(HttpMethod.Post, $"/manifests/{manifest}/propose", operatorToken, null, HttpStatusCode.NoContent);
        await ExpectAsync(HttpMethod.Post, $"/manifests/{manifest}/approve", admin, null, HttpStatusCode.NoContent);
        await ExpectAsync(HttpMethod.Post, $"/manifests/{manifest}/prepare", operatorToken, null, HttpStatusCode.NoContent);
        await ExpectAsync(HttpMethod.Post, $"/manifests/{manifest}/ready", operatorToken, null, HttpStatusCode.NoContent);

        await departure.WhenIMakeTheConvoyReadyToDepart();
        await ExpectAsync(HttpMethod.Post, $"{ConvoyPath}/depart", operatorToken, null, HttpStatusCode.OK);
    }

    private async Task ExpectAsync(HttpMethod method, string path, string token, string? body, HttpStatusCode expected)
    {
        var response = await api.SendAsync(method, path, token, body);

        response.StatusCode.Should().Be(expected, "{0} {1} said: {2}", method, path, api.LastBody);
    }

    [Given("a registered hub exists")]
    public async Task GivenARegisteredHubExists()
    {
        var admin = await api.TokenForAsync("admin");
        var created = await api.SendAsync(
            HttpMethod.Post, "/locations", admin, """{ "name": "BDD hub", "isRegisteredHub": true }""");
        created.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);
        var location = created.Headers.Location!;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.ToString();
        var id = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];
        state.CreatedResources.Add(("locations", id));
        state.Pin(HubKey, id);
    }

    [When("I mark the insured vehicle delivered")]
    public Task WhenIMarkTheInsuredVehicleDelivered() =>
        api.SendAsync(HttpMethod.Post, $"{VehiclePath}/delivered", state.CurrentToken, null);

    [When("I accept the goods list of the registered receiver on the insured vehicle")]
    public Task WhenIAcceptTheGoodsList() =>
        api.SendAsync(
            HttpMethod.Post,
            $"{VehiclePath}/declarations/goods-list/{state.Pinned(DepartureSteps.ReceiverKey)}/accepted",
            state.CurrentToken,
            null);

    [When("I record the outcome \"(.*)\" for the remembered box")]
    public Task WhenIRecordTheOutcome(string outcome) =>
        api.SendAsync(
            HttpMethod.Post,
            $"{ConvoyPath}/boxes/{state.Pinned("box")}/outcome",
            state.CurrentToken,
            $$"""{ "outcome": "{{outcome}}" }""");

    [When("I return the remembered box to the registered hub")]
    public Task WhenIReturnTheBoxToTheHub() =>
        api.SendAsync(
            HttpMethod.Post,
            $"{ConvoyPath}/boxes/{state.Pinned("box")}/outcome",
            state.CurrentToken,
            $$"""{ "outcome": "ReturnedToHub", "hubLocationId": {{state.Pinned(HubKey)}} }""");

    [When("I return the remembered box to a location that does not exist")]
    public Task WhenIReturnTheBoxToNowhere() =>
        api.SendAsync(
            HttpMethod.Post,
            $"{ConvoyPath}/boxes/{state.Pinned("box")}/outcome",
            state.CurrentToken,
            """{ "outcome": "ReturnedToHub", "hubLocationId": 0 }""");

    [When("I close the convoy of the insured vehicle")]
    public Task WhenICloseTheConvoy() =>
        api.SendAsync(HttpMethod.Post, $"{ConvoyPath}/close", state.CurrentToken, null);

    [When("I GET \"(.*)\" on the convoy of the insured vehicle")]
    public Task WhenIGetOnTheConvoy(string suffix) =>
        api.SendAsync(HttpMethod.Get, ConvoyPath + suffix, state.CurrentToken, null);

    [Then("the remembered box has the outcome \"(.*)\"")]
    public async Task ThenTheBoxHasTheOutcome(string outcome)
    {
        var response = await api.SendAsync(HttpMethod.Get, $"{ConvoyPath}/outcomes", state.CurrentToken, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the body was: {0}", api.LastBody);
        var boxId = int.Parse(state.Pinned("box"));
        JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Single(item => item.GetProperty("boxId").GetInt32() == boxId)
            .GetProperty("outcome").GetString().Should().Be(outcome, "the body was: {0}", api.LastBody);
    }

    [Then("the vehicle no longer carries the remembered box")]
    public async Task ThenTheVehicleNoLongerCarriesTheBox()
    {
        await api.SendAsync(HttpMethod.Get, $"{VehiclePath}/boxes", state.CurrentToken, null);

        var boxId = int.Parse(state.Pinned("box"));
        JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Select(box => box.GetProperty("boxId").GetInt32())
            .Should().NotContain(boxId, "the body was: {0}", api.LastBody);
    }

    [Then("the remembered box is at the registered hub")]
    public async Task ThenTheBoxIsAtTheHub()
    {
        var response = await api.SendAsync(HttpMethod.Get, $"/boxes/{state.Pinned("box")}", state.CurrentToken, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the body was: {0}", api.LastBody);
        JsonDocument.Parse(api.LastBody).RootElement
            .GetProperty("locationId").GetInt32().ToString().Should().Be(state.Pinned(HubKey));
    }

    [Then("the report names no address, contact or receiver identifier")]
    public void ThenTheReportNamesNoAddress()
    {
        var names = PropertyNames(JsonDocument.Parse(api.LastBody).RootElement).ToList();

        names.Should().NotContain(name =>
            name.Contains("address") || name.Contains("street") || name.Contains("city") || name.Contains("postcode")
            || name.Contains("house") || name.Contains("phone") || name.Contains("contact") || name.Contains("email")
            || name.Contains("receiverref") || name.Contains("delivery"),
            "the body was: {0}", api.LastBody);
        api.LastBody.Should().NotContain(state.Pinned(DepartureSteps.ReceiverKey), "a receiver is never identified in the report");
    }

    [Then("the report counts (\\d+) box(?:es)? delivered and (\\d+) not delivered")]
    public void ThenTheReportCounts(int delivered, int notDelivered)
    {
        var totals = JsonDocument.Parse(api.LastBody).RootElement.GetProperty("totals");

        totals.GetProperty("boxesDelivered").GetInt32().Should().Be(delivered, "the body was: {0}", api.LastBody);
        totals.GetProperty("boxesNotDelivered").GetInt32().Should().Be(notDelivered, "the body was: {0}", api.LastBody);
    }

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .SelectMany(property => PropertyNames(property.Value).Prepend(property.Name.ToLowerInvariant())),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };
}
