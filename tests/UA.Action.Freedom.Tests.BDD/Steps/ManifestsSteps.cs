using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>
/// Steps particular to <c>/manifests</c>. The generic HTTP and authentication steps live in
/// <see cref="ApiSteps"/>.
/// </summary>
/// <remarks>
/// A manifest reference is a natural key the caller supplies, so scenarios need a fresh one
/// each run rather than a literal — otherwise a second run collides with the first. The convoy
/// steps here exist because the truck-list precondition can only be exercised against a real
/// convoy in a known state.
/// </remarks>
[Binding]
public sealed class ManifestsSteps(FreedomApiClient api, ScenarioState state)
{
    private const string ManifestKey = "manifest";
    private const string ConvoyKey = "convoy-for-manifest";
    private const string VehicleKey = "vehicle-for-manifest";

    private const string ConvoyBody =
        """
        { "start": "2026-09-01T06:00:00Z", "expectedEnd": "2026-09-05T18:00:00Z" }
        """;

    [Given("a manifest reference that is not yet used")]
    public void GivenAManifestReferenceThatIsNotYetUsed() =>
        state.Pin(ManifestKey, "BDD" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant());

    [Given("a convoy exists whose truck list is published")]
    public async Task GivenAConvoyExistsWhoseTruckListIsPublished()
    {
        await CreateConvoy();

        var published = await api.SendAsync(
            HttpMethod.Post, $"/convoys/{state.Pinned(ConvoyKey)}/publish-truck-list", state.CurrentToken, null);

        published.StatusCode.Should().Be(HttpStatusCode.NoContent, "the body was: {0}", api.LastBody);
    }

    /// <summary>
    /// Everything departure needs: a vehicle that passed its inspection, on the convoy, insured
    /// for today — and then the truck list published. Insurance is recorded before publishing
    /// only because nothing here changes the crew afterwards; a crew change would void it.
    /// </summary>
    [Given("a convoy exists with an insured vehicle on its published truck list")]
    public async Task GivenAConvoyExistsWithAnInsuredVehicleOnItsPublishedTruckList()
    {
        await CreateConvoy();
        var convoyId = state.Pinned(ConvoyKey);
        var vin = "BDDM" + Guid.NewGuid().ToString("N")[..13].ToUpperInvariant();
        var operatorToken = await api.TokenForAsync("operator");

        (await api.SendAsync(HttpMethod.Post, "/vehicles", operatorToken, $$"""
            { "vin": "{{vin}}", "plate": "UA20ACT", "year": 2015, "fuel": "Diesel", "transmission": "Manual", "weightKg": 2100 }
            """)).StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);
        state.CreatedResources.Add(("vehicles", vin));

        (await api.SendAsync(HttpMethod.Put, $"/vehicles/{vin}/inspection", operatorToken, """{ "status": "Passed" }"""))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "the body was: {0}", api.LastBody);
        (await api.SendAsync(HttpMethod.Put, $"/convoys/{convoyId}/vehicles/{vin}", operatorToken, null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "the body was: {0}", api.LastBody);

        var today = DateTime.UtcNow.Date;
        (await api.SendAsync(HttpMethod.Put, $"/convoys/{convoyId}/vehicles/{vin}/insurance", operatorToken, $$"""
            { "insurer": "Ukraine Aid Mutual", "policyNumber": "BDD-1", "coverStart": "{{today.AddDays(-1):yyyy-MM-dd}}", "coverEnd": "{{today.AddDays(30):yyyy-MM-dd}}" }
            """)).StatusCode.Should().Be(HttpStatusCode.NoContent, "the body was: {0}", api.LastBody);

        (await api.SendAsync(HttpMethod.Post, $"/convoys/{convoyId}/publish-truck-list", operatorToken, null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "the body was: {0}", api.LastBody);

        state.Pin(VehicleKey, vin);
    }

    [When("I POST a manifest for the insured vehicle on the remembered convoy")]
    public Task WhenIPostAManifestForTheInsuredVehicle() =>
        PostManifest($$"""
            { "id": "{{state.Pinned(ManifestKey)}}", "convoyId": {{state.Pinned(ConvoyKey)}}, "vin": "{{state.Pinned(VehicleKey)}}" }
            """);

    [When("I remove the insurance of the insured vehicle")]
    public async Task WhenIRemoveTheInsuranceOfTheInsuredVehicle()
    {
        var operatorToken = await api.TokenForAsync("operator");
        (await api.SendAsync(
                HttpMethod.Delete,
                $"/convoys/{state.Pinned(ConvoyKey)}/vehicles/{state.Pinned(VehicleKey)}/insurance",
                operatorToken,
                null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "the body was: {0}", api.LastBody);
    }

    [When("I mark the manifest's convoy arrived")]
    public Task WhenIMarkTheManifestsConvoyArrived() =>
        api.SendAsync(HttpMethod.Post, $"/convoys/{state.Pinned(ConvoyKey)}/arrive", state.CurrentToken, null);

    [Then("the insured vehicle has been handed over")]
    public async Task ThenTheInsuredVehicleHasBeenHandedOver()
    {
        var response = await api.SendAsync(HttpMethod.Get, $"/vehicles/{state.Pinned(VehicleKey)}", state.CurrentToken, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(api.LastBody).RootElement
            .GetProperty("handedOverAt").ValueKind.Should().Be(JsonValueKind.String, "the body was: {0}", api.LastBody);
    }

    [Then("the insured vehicle cannot join another convoy")]
    public async Task ThenTheInsuredVehicleCannotJoinAnotherConvoy()
    {
        var created = await api.SendAsync(HttpMethod.Post, "/convoys", state.CurrentToken, ConvoyBody);
        created.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);
        var location = created.Headers.Location!;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.ToString();
        var next = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];
        state.CreatedResources.Add(("convoys", next));

        var response = await api.SendAsync(
            HttpMethod.Put, $"/convoys/{next}/vehicles/{state.Pinned(VehicleKey)}", state.CurrentToken, null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "the body was: {0}", api.LastBody);
    }

    [Given("a convoy exists whose truck list is not published")]
    public Task GivenAConvoyExistsWhoseTruckListIsNotPublished() => CreateConvoy();

    [When("I POST a manifest with no convoy")]
    public Task WhenIPostAManifestWithNoConvoy() =>
        PostManifest($$"""{ "id": "{{state.Pinned(ManifestKey)}}" }""");

    [When("I POST a manifest on the remembered convoy")]
    public Task WhenIPostAManifestOnTheRememberedConvoy() =>
        PostManifest($$"""
            { "id": "{{state.Pinned(ManifestKey)}}", "convoyId": {{state.Pinned(ConvoyKey)}} }
            """);

    [When("I GET the remembered manifest")]
    public Task WhenIGetTheRememberedManifest() =>
        api.SendAsync(HttpMethod.Get, ManifestPath(), state.CurrentToken, null);

    [When("I GET \"(.*)\" on the remembered manifest")]
    public Task WhenIGetOnTheRememberedManifest(string suffix) =>
        api.SendAsync(HttpMethod.Get, ManifestPath(suffix), state.CurrentToken, null);

    [When("I POST \"(.*)\" on the remembered manifest")]
    public Task WhenIPostTransitionOnTheRememberedManifest(string transition) =>
        api.SendAsync(HttpMethod.Post, ManifestPath($"/{transition}"), state.CurrentToken, null);

    [When("I PUT the remembered manifest with body:")]
    public Task WhenIPutTheRememberedManifestWithBody(string body) =>
        api.SendAsync(HttpMethod.Put, ManifestPath(), state.CurrentToken, body);

    [When("I DELETE the remembered manifest")]
    public Task WhenIDeleteTheRememberedManifest() =>
        api.SendAsync(HttpMethod.Delete, ManifestPath(), state.CurrentToken, null);

    private string ManifestPath(string suffix = "") => $"/manifests/{state.Pinned(ManifestKey)}{suffix}";

    private async Task PostManifest(string body)
    {
        var response = await api.SendAsync(HttpMethod.Post, "/manifests", state.CurrentToken, body);

        if (response.StatusCode == HttpStatusCode.Created)
        {
            state.CreatedResources.Add(("manifests", state.Pinned(ManifestKey)));
        }
    }

    private async Task CreateConvoy()
    {
        var response = await api.SendAsync(HttpMethod.Post, "/convoys", state.CurrentToken, ConvoyBody);

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);

        var location = response.Headers.Location!;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.ToString();
        var convoyId = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];

        state.CreatedResources.Add(("convoys", convoyId));
        state.Pin(ConvoyKey, convoyId);
    }
}
