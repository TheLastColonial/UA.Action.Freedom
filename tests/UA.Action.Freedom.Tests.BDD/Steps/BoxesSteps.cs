using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>
/// Steps particular to <c>/boxes</c>. The generic HTTP and authentication steps live in
/// <see cref="ApiSteps"/>.
/// </summary>
/// <remarks>
/// Validating a box and shelving it are signed as the caller's linked volunteer, so these steps
/// name nobody: the seed logins are linked once per run by <see cref="LoginLinkHooks"/>.
/// </remarks>
[Binding]
public sealed class BoxesSteps(FreedomApiClient api, ScenarioState state)
{
    [Given("I remember the box")]
    public void GivenIRememberTheBox() => state.Remember("box");

    [When("I POST \"(.*)\" at the remembered location")]
    public async Task WhenIPostAtTheRememberedLocation(string path)
    {
        var body = $$"""{ "locationId": {{state.Pinned(LocationsSteps.LocationKey)}} }""";
        var response = await api.SendAsync(HttpMethod.Post, path, state.CurrentToken, body);

        if (response.StatusCode == HttpStatusCode.Created && response.Headers.Location is not null)
        {
            var location = response.Headers.Location;
            var resolved = location.IsAbsoluteUri ? location.AbsolutePath : location.ToString();
            var id = resolved.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];

            state.CreatedResources.Add(("boxes", id));
            state.LastCreatedKey = id;
        }
    }

    [When("I PUT \"(.*)\" on the remembered box with the remembered bay")]
    public Task WhenIPutTheRememberedBoxInTheRememberedBay(string template)
    {
        var body = $$"""
            { "bayId": {{state.Pinned(LocationsSteps.BayKey)}} }
            """;

        return api.SendAsync(HttpMethod.Put, state.Recall("box", template), state.CurrentToken, body);
    }

    [Then("the response body field \"(.*)\" is the remembered bay")]
    public void ThenTheResponseBodyFieldIsTheRememberedBay(string field) =>
        JsonDocument.Parse(api.LastBody).RootElement.GetProperty(field).GetInt32()
            .Should().Be(int.Parse(state.Pinned(LocationsSteps.BayKey)));

    [When("I GET \"(.*)\" on the remembered box")]
    public Task WhenIGetOnTheRememberedBox(string template) =>
        api.SendAsync(HttpMethod.Get, state.Recall("box", template), state.CurrentToken, null);

    [When("I PUT \"(.*)\" on the remembered box with body:")]
    public Task WhenIPutOnTheRememberedBoxWithBody(string template, string body) =>
        api.SendAsync(HttpMethod.Put, state.Recall("box", template), state.CurrentToken, body);

    [When("I POST \"(.*)\" on the remembered box with body:")]
    public Task WhenIPostOnTheRememberedBoxWithBody(string template, string body) =>
        api.SendAsync(HttpMethod.Post, state.Recall("box", template), state.CurrentToken, body);

    [When("I POST \"(.*)\" on the remembered box")]
    public Task WhenIPostOnTheRememberedBox(string template) =>
        api.SendAsync(HttpMethod.Post, state.Recall("box", template), state.CurrentToken, null);

    [When("I DELETE \"(.*)\" on the remembered box")]
    public Task WhenIDeleteOnTheRememberedBox(string template) =>
        api.SendAsync(HttpMethod.Delete, state.Recall("box", template), state.CurrentToken, null);

    [Given("I remember the issued QR token")]
    public void GivenIRememberTheIssuedQrToken()
    {
        // Read it straight off the Location header the issue call returned: /boxes/scan/{token}.
        var location = api.LastResponse!.Headers.Location
            ?? throw new InvalidOperationException("The last response carried no Location header to read a QR token from.");
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.ToString();

        state.Pin("qrtoken", path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1]);
    }

    [When("I GET \"(.*)\" for the remembered QR token")]
    public Task WhenIGetForTheRememberedQrToken(string template) =>
        api.SendAsync(HttpMethod.Get, state.Recall("qrtoken", template), state.CurrentToken, null);

    [When("I POST \"(.*)\" on the remembered box weighing (\\d+)")]
    public Task WhenIValidateTheRememberedBox(string template, int weightKg)
    {
        var body = $$"""
            { "weightKg": {{weightKg}} }
            """;

        return api.SendAsync(HttpMethod.Post, state.Recall("box", template), state.CurrentToken, body);
    }

    [Then("the response body lists an item described as \"(.*)\"")]
    public void ThenTheResponseBodyListsAnItemDescribedAs(string description) =>
        JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Any(element => element.GetProperty("description").GetString() == description)
            .Should().BeTrue("the body was: {0}", api.LastBody);
}
