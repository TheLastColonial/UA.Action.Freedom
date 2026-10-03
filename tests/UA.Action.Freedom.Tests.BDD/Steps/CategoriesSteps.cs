using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>
/// Steps particular to <c>/categories</c>, and the fixtures the item scenarios in <c>Boxes.feature</c> need: an item
/// names its category, and only an Administrator can add one. The generic HTTP and authentication steps live in
/// <see cref="ApiSteps"/>.
/// </summary>
/// <remarks>
/// There is no <c>DELETE /categories</c>, so a scenario cannot clean up after itself the way the other fixtures do.
/// Each fixture is therefore found by name and created only if it is missing, which leaves at most one row per fixture
/// however many times the suite runs, rather than one per scenario.
/// </remarks>
[Binding]
public sealed class CategoriesSteps(FreedomApiClient api, ScenarioState state)
{
    internal const string CategoryKey = "category";

    private const string OrdinaryName = "BDD Category";

    private const string NotCarriedName = "BDD Not Carried";

    /// <summary>
    /// Fills the placeholders a body may use: <c>{category}</c> is the remembered category and <c>{yesterday}</c> is a
    /// date that has already passed.
    /// </summary>
    internal static string Expand(ScenarioState state, string text) => text
        .Replace("{category}", state.TryPinned(CategoryKey) ?? "missing-category", StringComparison.Ordinal)
        .Replace("{yesterday}", DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"), StringComparison.Ordinal);

    [Given("a category exists")]
    public Task GivenACategoryExists() => RememberCategoryAsync(OrdinaryName, isNotCarried: false);

    [Given("a category exists that the convoy will not carry")]
    public Task GivenANotCarriedCategoryExists() => RememberCategoryAsync(NotCarriedName, isNotCarried: true);

    [Given("the category maps to the EU code \"(.*)\"")]
    public async Task GivenTheCategoryMapsToTheEuCode(string code)
    {
        var admin = await api.TokenForAsync("admin");

        var response = await api.SendAsync(
            HttpMethod.Put,
            $"/categories/{state.Pinned(CategoryKey)}/codes/EU",
            admin,
            JsonSerializer.Serialize(new { code }));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "the body was: {0}", api.LastBody);
    }

    [When("I GET \"(.*)\" on the remembered category")]
    public Task WhenIGetOnTheRememberedCategory(string template) =>
        api.SendAsync(HttpMethod.Get, state.Recall(CategoryKey, template), state.CurrentToken, null);

    [When("I PUT \"(.*)\" on the remembered category with body:")]
    public Task WhenIPutOnTheRememberedCategoryWithBody(string template, string body) =>
        api.SendAsync(HttpMethod.Put, state.Recall(CategoryKey, template), state.CurrentToken, body);

    [Then("the response body mentions \"(.*)\"")]
    public void ThenTheResponseBodyMentions(string value) =>
        api.LastBody.Should().Contain(value);

    [Then("the filing sheet declares commodity code \"(.*)\" for \"(.*)\"")]
    public void ThenTheFilingSheetDeclaresCommodityCodeFor(string code, string description)
    {
        var sheet = JsonDocument.Parse(api.LastBody).RootElement;

        var item = sheet.GetProperty("consignments").EnumerateArray()
            .SelectMany(consignment => consignment.GetProperty("goodsItems").EnumerateArray())
            .Single(candidate => candidate.GetProperty("description").GetString() == description);

        item.GetProperty("commodityCode").GetString().Should().Be(code, "the sheet was: {0}", api.LastBody);
        sheet.GetProperty("missing").EnumerateArray()
            .Select(gap => gap.GetString())
            .Should().NotContain(gap => gap!.Contains(description), "the category supplied the code");
    }

    private async Task RememberCategoryAsync(string name, bool isNotCarried)
    {
        var admin = await api.TokenForAsync("admin");

        await api.SendAsync(HttpMethod.Get, "/categories", admin, null);
        var existing = JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Where(category => category.GetProperty("nameEn").GetString() == name)
            .Select(category => (int?)category.GetProperty("id").GetInt32())
            .FirstOrDefault();

        if (existing is { } id)
        {
            state.Pin(CategoryKey, id.ToString());
            return;
        }

        var response = await api.SendAsync(
            HttpMethod.Post,
            "/categories",
            admin,
            JsonSerializer.Serialize(new { nameEn = name, isSensitive = false, isNotCarried }));

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);

        var path = response.Headers.Location!.IsAbsoluteUri
            ? response.Headers.Location.AbsolutePath
            : response.Headers.Location.ToString();
        state.Pin(CategoryKey, path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1]);
    }
}
