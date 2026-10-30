using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>
/// Steps particular to resource-scoped permissions (ADR 0010): a Loader who manages one location, boxes at another,
/// and a seed login that is a Convoy Leader only once a Dispatcher nominates the volunteer it is linked to.
/// </summary>
[Binding]
public sealed class ScopedPermissionsSteps(FreedomApiClient api, ScenarioState state)
{
    private const string DriverKey = "driver";

    /// <summary>The seed login's own volunteer, from <c>GET /me</c>: the linking is done once per run by <see cref="LoginLinkHooks"/>.</summary>
    private async Task<string> PersonOfAsync(string login)
    {
        var token = await api.TokenForAsync(login);
        var response = await api.SendAsync(HttpMethod.Get, "/me", token, null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the body was: {0}", api.LastBody);

        var personId = JsonDocument.Parse(api.LastBody).RootElement.GetProperty("personId");
        personId.ValueKind.Should().NotBe(JsonValueKind.Null, "the {0} login must be linked to a volunteer", login);
        return personId.GetString()!;
    }

    private async Task<string> CreateAsAdminAsync(string route, string body, string resource)
    {
        var admin = await api.TokenForAsync("admin");
        var response = await api.SendAsync(HttpMethod.Post, route, admin, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);

        var location = response.Headers.Location!;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.ToString();
        var id = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];
        state.CreatedResources.Add((resource, id));
        return id;
    }

    [Given("the \"(.*)\" login's volunteer is the driver")]
    public async Task GivenTheLoginsVolunteerIsTheDriver(string login) =>
        state.Pin(DriverKey, await PersonOfAsync(login));

    [Given("the \"(.*)\" login manages a location")]
    public async Task GivenTheLoginManagesALocation(string login)
    {
        var personId = await PersonOfAsync(login);
        var locationId = await CreateAsAdminAsync("/locations", """{ "name": "BDD Scoped Depot" }""", "locations");
        var admin = await api.TokenForAsync("admin");

        var response = await api.SendAsync(HttpMethod.Put, $"/locations/{locationId}/loaders/{personId}", admin, null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "the body was: {0}", api.LastBody);
        state.Pin("mine", locationId);
    }

    [Given("a location the loader does not manage exists")]
    public async Task GivenALocationTheLoaderDoesNotManageExists() =>
        state.Pin("theirs", await CreateAsAdminAsync("/locations", """{ "name": "BDD Other Depot" }""", "locations"));

    [Given("a box exists at the location the loader manages")]
    public async Task GivenABoxExistsAtTheLocationTheLoaderManages() =>
        state.Pin("myBox", await CreateAsAdminAsync("/boxes", $$"""{ "locationId": {{state.Pinned("mine")}} }""", "boxes"));

    [Given("a box exists at the location the loader does not manage")]
    public async Task GivenABoxExistsAtTheLocationTheLoaderDoesNotManage() =>
        state.Pin("theirBox", await CreateAsAdminAsync("/boxes", $$"""{ "locationId": {{state.Pinned("theirs")}} }""", "boxes"));

    [When("I fetch \"(.*)\" using the pinned \"(.*)\"")]
    public Task WhenIGetForThePinned(string template, string name) =>
        api.SendAsync(HttpMethod.Get, template.Replace("{pinned}", state.Pinned(name), StringComparison.Ordinal), state.CurrentToken, null);

    [When("I post \"(.*)\" using the pinned \"(.*)\"")]
    public Task WhenIPostForThePinned(string template, string name) =>
        api.SendAsync(HttpMethod.Post, template.Replace("{pinned}", state.Pinned(name), StringComparison.Ordinal), state.CurrentToken, null);

    [Then("the response lists the pinned \"(.*)\"")]
    public void ThenTheResponseListsThePinned(string name) =>
        IdsInBody().Should().Contain(state.Pinned(name), "the body was: {0}", api.LastBody);

    [Then("the response does not list the pinned \"(.*)\"")]
    public void ThenTheResponseDoesNotListThePinned(string name) =>
        IdsInBody().Should().NotContain(state.Pinned(name), "the body was: {0}", api.LastBody);

    private IEnumerable<string> IdsInBody() =>
        JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Select(row => row.GetProperty("id").ToString());
}
