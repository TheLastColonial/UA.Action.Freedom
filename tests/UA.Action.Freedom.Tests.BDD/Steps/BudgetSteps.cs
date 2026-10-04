using System.Globalization;
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>Steps particular to the convoy budget, its costs and vehicle equipment.</summary>
[Binding]
public sealed class BudgetSteps(FreedomApiClient api, ScenarioState state)
{
    [When("I POST \"(.*)\" on the remembered convoy with body:")]
    public Task WhenIPostOnTheRememberedConvoyWithBody(string template, string body) =>
        api.SendAsync(HttpMethod.Post, state.Recall("convoy", template), state.CurrentToken, body);

    /// <summary>
    /// A catalogue entry with a name of its own, because the catalogue has no delete and a name is unique.
    /// </summary>
    [Given("a catalogued equipment item priced at (.*)")]
    public async Task GivenACataloguedEquipmentItemPricedAt(string unitCost)
    {
        var token = state.CurrentToken ?? await api.TokenForAsync("operator");
        var name = "BDD equipment " + Guid.NewGuid().ToString("N")[..12];
        var response = await api.SendAsync(
            HttpMethod.Post,
            "/equipment-items",
            token,
            $$"""{ "name": "{{name}}", "unitCostGbp": {{unitCost}} }""");

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);
        state.Pin("equipmentItem", response.Headers.Location!.ToString().Split('/')[^1]);
    }

    [When(@"I PUT ""(.*)"" on the remembered convoy with (\d+) of the catalogued equipment item")]
    public Task WhenIPutEquipmentOnTheRememberedConvoy(string template, int quantity) =>
        api.SendAsync(
            HttpMethod.Put,
            state.Recall("convoy", template),
            state.CurrentToken,
            $$"""{ "lines": [ { "equipmentItemId": {{state.Pinned("equipmentItem")}}, "quantity": {{quantity}} } ] }""");

    [Then("the \"(.*)\" line of the summary has an actual of (.*)")]
    public void ThenTheLineHasAnActualOf(string type, string expected) =>
        LineOf(type).GetProperty("actualGbp").GetDecimal()
            .Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture), "the body was: {0}", api.LastBody);

    [Then("the \"(.*)\" line of the summary is over budget")]
    public void ThenTheLineIsOverBudget(string type) =>
        LineOf(type).GetProperty("overBudget").GetBoolean().Should().BeTrue("the body was: {0}", api.LastBody);

    [Then("the readiness advises \"(.*)\"")]
    public void ThenTheReadinessAdvises(string advice) =>
        JsonDocument.Parse(api.LastBody).RootElement.GetProperty("advisories").EnumerateArray()
            .Select(element => element.GetString())
            .Should().Contain(advice, "the body was: {0}", api.LastBody);

    private JsonElement LineOf(string type) =>
        JsonDocument.Parse(api.LastBody).RootElement.GetProperty("lines").EnumerateArray()
            .Single(line => line.GetProperty("type").GetString() == type);
}
