using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>
/// The steps every feature shares: reaching the deployed API, authenticating as one of the seed
/// logins, issuing a request, and asserting on the response.
/// </summary>
/// <remarks>
/// Separate from the per-resource step classes because Reqnroll matches step text globally — two
/// classes both defining <c>the response status is (\d+)</c> would be an ambiguous binding, not
/// an override.
/// </remarks>
[Binding]
public sealed class ApiSteps(FreedomApiClient api, ScenarioState state)
{
    [Given("the Freedom API is reachable")]
    public async Task GivenTheFreedomApiIsReachable()
    {
        var (ok, reason) = await api.ProbeAsync();
        Assert.SkipUnless(ok, reason);
    }

    [Given("the Freedom API exposes \"(.*)\"")]
    public async Task GivenTheFreedomApiExposes(string route)
    {
        var (ok, reason) = await api.ProbeAsync(route);
        Assert.SkipUnless(ok, reason);
    }

    [Given("I am authenticated as \"(.*)\"")]
    public async Task GivenIAmAuthenticatedAs(string username)
    {
        state.CurrentToken = await api.TokenForAsync(username);
    }

    [When("I GET \"(.*)\" without a token")]
    public Task WhenIGetWithoutAToken(string path) =>
        api.SendAsync(HttpMethod.Get, path, bearerToken: null, jsonBody: null);

    [When("I GET \"(.*)\"")]
    public Task WhenIGet(string path) =>
        api.SendAsync(HttpMethod.Get, path, state.CurrentToken, jsonBody: null);

    [When("I POST \"(.*)\" with body:")]
    public async Task WhenIPostWithBody(string path, string body)
    {
        var response = await api.SendAsync(HttpMethod.Post, path, state.CurrentToken, body);
        TrackCreated(response);
    }

    [When("I PUT \"(.*)\" with body:")]
    public Task WhenIPutWithBody(string path, string body) =>
        api.SendAsync(HttpMethod.Put, path, state.CurrentToken, body);

    [When("I DELETE \"(.*)\"")]
    public Task WhenIDelete(string path) =>
        api.SendAsync(HttpMethod.Delete, path, state.CurrentToken, jsonBody: null);

    [Then("the response status is (\\d+)")]
    public void ThenTheResponseStatusIs(int expected) =>
        ((int)api.LastResponse!.StatusCode).Should().Be(expected, "the body was: {0}", api.LastBody);

    [Then("the \"(.*)\" header ends with \"(.*)\"")]
    public void ThenTheHeaderEndsWith(string header, string suffix)
    {
        api.LastResponse!.Headers.TryGetValues(header, out var values).Should().BeTrue($"'{header}' header should be present");
        string.Join(",", values!).Should().EndWith(suffix);
    }

    [Then("the \"(.*)\" header names a new resource")]
    public void ThenTheHeaderNamesANewResource(string header)
    {
        api.LastResponse!.Headers.TryGetValues(header, out var values).Should().BeTrue($"'{header}' header should be present");
        string.Join(",", values!).Should().NotBeEmpty();
    }

    [Then("the response body field \"(.*)\" is \"(.*)\"")]
    public void ThenTheResponseBodyFieldIs(string field, string expected)
    {
        var element = JsonDocument.Parse(api.LastBody).RootElement.GetProperty(field);
        var actual = element.ValueKind == JsonValueKind.Number ? element.GetRawText() : element.ToString();
        actual.Should().Be(expected);
    }

    [Then("the response body names \"(.*)\" as invalid")]
    public void ThenTheResponseBodyNamesAsInvalid(string field) =>
        JsonDocument.Parse(api.LastBody).RootElement
            .GetProperty("errors")
            .TryGetProperty(field, out _)
            .Should().BeTrue("the body was: {0}", api.LastBody);

    [Then("the response body does not mention \"(.*)\"")]
    public void ThenTheResponseBodyDoesNotMention(string value) =>
        api.LastBody.Should().NotContain(value);

    [Then("the response body is a list of (\\d+) or more")]
    public void ThenTheResponseBodyIsAListOfOrMore(int count) =>
        JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray().Count()
            .Should().BeGreaterThanOrEqualTo(count, "the body was: {0}", api.LastBody);

    /// <summary>
    /// Notes a created resource from its <c>Location</c> header so the cleanup hook can remove
    /// it. Reading the header rather than the request body is what makes this work for a slice
    /// whose identifier the server mints — <c>/people</c> — as well as one keyed on a field the
    /// caller supplied.
    /// </summary>
    private void TrackCreated(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Created || response.Headers.Location is null)
        {
            return;
        }

        var location = response.Headers.Location;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.ToString();
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length >= 2)
        {
            state.CreatedResources.Add((segments[^2], segments[^1]));
        }
    }
}
