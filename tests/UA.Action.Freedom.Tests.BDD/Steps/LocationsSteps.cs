using System.Net;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>
/// Steps particular to <c>/locations</c> and <c>/locations/{id}/bays</c>, and the fixtures
/// <c>Boxes.feature</c> needs to exercise bay allocation. The generic HTTP and authentication
/// steps live in <see cref="ApiSteps"/>.
/// </summary>
[Binding]
public sealed class LocationsSteps(FreedomApiClient api, ScenarioState state)
{
    internal const string LocationKey = "location";
    internal const string OtherLocationKey = "otherLocation";
    internal const string BayKey = "bay";

    [Given("a location exists")]
    public async Task GivenALocationExists()
    {
        // Only an Administrator may set up a distribution hub, whatever identity the scenario
        // is otherwise using.
        var admin = await api.TokenForAsync("admin");

        var response = await api.SendAsync(HttpMethod.Post, "/locations", admin, """{ "name": "BDD Depot" }""");

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);

        var id = LastPathSegment(response);
        state.CreatedResources.Add(("locations", id));
        state.Pin(LocationKey, id);
    }

    [Given("a second location exists")]
    public async Task GivenASecondLocationExists()
    {
        var admin = await api.TokenForAsync("admin");

        var response = await api.SendAsync(HttpMethod.Post, "/locations", admin, """{ "name": "BDD Depot 2" }""");

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);

        var id = LastPathSegment(response);
        state.CreatedResources.Add(("locations", id));
        state.Pin(OtherLocationKey, id);
    }

    [Given("a bay exists at the location")]
    public async Task GivenABayExistsAtTheLocation()
    {
        var admin = await api.TokenForAsync("admin");
        var locationId = state.Pinned(LocationKey);

        var response = await api.SendAsync(
            HttpMethod.Post, $"/locations/{locationId}/bays", admin, """{ "code": "A1" }""");

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);

        // Not added to CreatedResources: the location's own cleanup cascades its bays away, and
        // a bay does not sit at a flat /{resource}/{key} route the cleanup hook assumes.
        state.Pin(BayKey, LastPathSegment(response));
    }

    [When("I GET \"(.*)\" on the remembered location")]
    public Task WhenIGetOnTheRememberedLocation(string template) =>
        api.SendAsync(HttpMethod.Get, state.Recall(LocationKey, template), state.CurrentToken, null);

    [When("I POST \"(.*)\" on the remembered location with body:")]
    public Task WhenIPostOnTheRememberedLocationWithBody(string template, string body) =>
        api.SendAsync(HttpMethod.Post, state.Recall(LocationKey, template), state.CurrentToken, body);

    private static string LastPathSegment(HttpResponseMessage response)
    {
        var location = response.Headers.Location!;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.ToString();
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];
    }
}
