using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

[Binding]
public sealed class VehiclesSteps(FreedomApiClient api, ScenarioState state)
{
    [Given("the Freedom API is reachable")]
    public async Task GivenTheFreedomApiIsReachable()
    {
        var (ok, reason) = await api.ProbeAsync();
        Assert.SkipUnless(ok, reason);
    }

    [Given("I am authenticated as \"(.*)\"")]
    public async Task GivenIAmAuthenticatedAs(string username)
    {
        state.CurrentToken = await api.TokenForAsync(username);
    }

    [Given("no vehicle exists with VIN \"(.*)\"")]
    public async Task GivenNoVehicleExistsWithVin(string vin)
    {
        var admin = await api.TokenForAsync("admin");
        await api.SendAsync(HttpMethod.Delete, $"/vehicles/{vin}", admin, null);
    }

    [Given("a vehicle exists with VIN \"(.*)\"")]
    public async Task GivenAVehicleExistsWithVin(string vin)
    {
        var token = state.CurrentToken ?? await api.TokenForAsync("operator");
        var body = $$"""
            { "vin": "{{vin}}", "plate": "UA10ACT", "year": 2014, "fuel": "Diesel", "transmission": "Manual", "weightKg": 2200 }
            """;

        var response = await api.SendAsync(HttpMethod.Post, "/vehicles", token, body);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
        state.CreatedVins.Add(vin);
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
        TrackCreatedVin(body, response);
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

    [Then("the response body field \"(.*)\" is \"(.*)\"")]
    public void ThenTheResponseBodyFieldIs(string field, string expected)
    {
        var element = JsonDocument.Parse(api.LastBody).RootElement.GetProperty(field);
        var actual = element.ValueKind == JsonValueKind.Number ? element.GetRawText() : element.ToString();
        actual.Should().Be(expected);
    }

    [Then("the response body lists a vehicle with VIN \"(.*)\"")]
    public void ThenTheResponseBodyListsAVehicleWithVin(string vin) =>
        JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Any(v => v.GetProperty("vin").GetString() == vin)
            .Should().BeTrue("the body was: {0}", api.LastBody);

    [Then("the response body names \"(.*)\" as invalid")]
    public void ThenTheResponseBodyNamesAsInvalid(string field) =>
        JsonDocument.Parse(api.LastBody).RootElement
            .GetProperty("errors")
            .TryGetProperty(field, out _)
            .Should().BeTrue("the body was: {0}", api.LastBody);

    private void TrackCreatedVin(string body, HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Created)
        {
            return;
        }

        if (JsonDocument.Parse(body).RootElement.TryGetProperty("vin", out var vin)
            && vin.GetString() is { Length: > 0 } value)
        {
            state.CreatedVins.Add(value);
        }
    }
}
