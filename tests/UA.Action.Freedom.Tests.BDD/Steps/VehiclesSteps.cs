using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>
/// Steps particular to <c>/vehicles</c>. The generic HTTP and authentication steps live in
/// <see cref="ApiSteps"/>.
/// </summary>
[Binding]
public sealed class VehiclesSteps(FreedomApiClient api, ScenarioState state)
{
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
        state.CreatedResources.Add(("vehicles", vin));
    }

    [Then("the response body lists a vehicle with VIN \"(.*)\"")]
    public void ThenTheResponseBodyListsAVehicleWithVin(string vin) =>
        JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Any(v => v.GetProperty("vin").GetString() == vin)
            .Should().BeTrue("the body was: {0}", api.LastBody);
}
