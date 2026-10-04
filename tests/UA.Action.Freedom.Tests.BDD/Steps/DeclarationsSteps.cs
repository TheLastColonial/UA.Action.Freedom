using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>
/// Steps for declaration staleness (ADR 0005): a declaration is stale when the load it was written from
/// differs from the load now, which the API derives on read.
/// </summary>
/// <remarks>
/// The convoy, vehicle and box come from <see cref="ManifestsSteps"/> and <see cref="BoxesSteps"/>, and
/// the generic declaration calls from <see cref="EnsSteps"/>; only phrasings new to this feature are here.
/// </remarks>
[Binding]
public sealed class DeclarationsSteps(FreedomApiClient api, ScenarioState state)
{
    private string Convoy => state.Pinned("convoy-for-manifest");

    private string Vin => state.Pinned("vehicle-for-manifest");

    private string DeclarationsPath => $"/convoys/{Convoy}/vehicles/{Vin}/declarations";

    [When("I take the remembered box off the insured vehicle")]
    public Task WhenITakeTheBoxOff() =>
        api.SendAsync(
            HttpMethod.Delete,
            $"/convoys/{Convoy}/vehicles/{Vin}/boxes/{state.Pinned("box")}",
            state.CurrentToken,
            null);

    private async Task<JsonElement?> CurrentAsync(string kind)
    {
        var response = await api.SendAsync(HttpMethod.Get, DeclarationsPath, state.CurrentToken, null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the body was: {0}", api.LastBody);

        return JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Where(declaration => declaration.GetProperty("kind").GetString() == kind
                                  && declaration.GetProperty("status").GetString() != "Withdrawn")
            .Select(declaration => (JsonElement?)declaration.Clone())
            .FirstOrDefault();
    }

    [Then("the \"(.*)\" declaration of the insured vehicle reads \"(.*)\"")]
    public async Task ThenTheDeclarationReads(string kind, string status)
    {
        var declaration = await CurrentAsync(kind);

        declaration.Should().NotBeNull($"a {kind} declaration should exist");
        declaration!.Value.GetProperty("status").GetString().Should().Be(status);
    }

    [Then("the convoy has (\\d+) re-declare tasks")]
    public async Task ThenTheConvoyHasTasks(int count)
    {
        var response = await api.SendAsync(HttpMethod.Get, $"/convoys/{Convoy}/tasks", state.CurrentToken, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the body was: {0}", api.LastBody);
        JsonDocument.Parse(api.LastBody).RootElement.GetArrayLength().Should().Be(count);
    }

    [When("I withdraw the stale \"(.*)\" declaration of the insured vehicle")]
    [When("I withdraw the \"(.*)\" declaration of the insured vehicle")]
    public async Task WhenIWithdraw(string kind)
    {
        var declaration = await CurrentAsync(kind);
        declaration.Should().NotBeNull($"a {kind} declaration should exist");

        await api.SendAsync(
            HttpMethod.Post,
            $"{DeclarationsPath}/{declaration!.Value.GetProperty("id").GetInt32()}/withdraw",
            state.CurrentToken,
            null);
    }
}
