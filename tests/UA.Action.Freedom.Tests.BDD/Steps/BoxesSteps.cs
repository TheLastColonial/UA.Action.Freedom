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
/// Validating a box needs a volunteer on file to name as the person who checked it — the
/// database will not accept a signature from somebody who does not exist. These steps create
/// one, remember it, and hand it to the validate calls.
/// </remarks>
[Binding]
public sealed class BoxesSteps(FreedomApiClient api, ScenarioState state)
{
    private const string ValidatorKey = "validator";

    [Given("a volunteer exists who can validate boxes")]
    public async Task GivenAVolunteerExistsWhoCanValidateBoxes()
    {
        // Only an Administrator may add a volunteer, whatever identity the scenario is using.
        var admin = await api.TokenForAsync("admin");

        var body = """
            { "firstName": "Sam", "lastName": "Whitfield", "dateOfBirth": "1990-01-01T00:00:00Z", "joined": "2024-01-01T00:00:00Z", "isDriver": false, "committed": false }
            """;

        var response = await api.SendAsync(HttpMethod.Post, "/people", admin, body);

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);

        var location = response.Headers.Location!;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.ToString();
        var personId = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];

        state.CreatedResources.Add(("people", personId));
        state.Pin(ValidatorKey, personId);
    }

    [Given("I remember the box")]
    public void GivenIRememberTheBox() => state.Remember("box");

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

    [When("I POST \"(.*)\" on the remembered box with the validating volunteer weighing (\\d+)")]
    public Task WhenIValidateTheRememberedBox(string template, int weightKg)
    {
        var body = $$"""
            { "validatedByPersonId": "{{state.Pinned(ValidatorKey)}}", "weightKg": {{weightKg}} }
            """;

        return api.SendAsync(HttpMethod.Post, state.Recall("box", template), state.CurrentToken, body);
    }

    [Then("the response body lists an item described as \"(.*)\"")]
    public void ThenTheResponseBodyListsAnItemDescribedAs(string description) =>
        JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Any(element => element.GetProperty("description").GetString() == description)
            .Should().BeTrue("the body was: {0}", api.LastBody);
}
