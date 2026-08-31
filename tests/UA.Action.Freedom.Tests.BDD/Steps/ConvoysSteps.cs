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
}
