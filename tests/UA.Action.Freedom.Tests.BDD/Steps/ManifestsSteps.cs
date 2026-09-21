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
    private const string CrewKey = "crew-for-manifest";

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
        await CreateConvoy(withVehicle: false);
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

        // Crewed before it is insured: the policy names the crew, and a later change would void it.
        var admin = await api.TokenForAsync("admin");
        var volunteer = await api.SendAsync(HttpMethod.Post, "/people", admin, """
            { "firstName": "Olena", "lastName": "Bondar", "dateOfBirth": "1985-01-01T00:00:00Z", "joined": "2024-01-01T00:00:00Z", "isDriver": true, "committed": true }
            """);
        volunteer.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);
        var volunteerPath = volunteer.Headers.Location!.IsAbsoluteUri ? volunteer.Headers.Location.AbsolutePath : volunteer.Headers.Location.ToString();
        var driverId = volunteerPath.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];
        state.CreatedResources.Add(("people", driverId));
        state.Pin(CrewKey, driverId);
        (await api.SendAsync(HttpMethod.Put, $"/convoys/{convoyId}/vehicles/{vin}/crew/{driverId}", operatorToken, """{ "leg": "Uk" }"""))
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
    public Task WhenIPostAManifestForTheInsuredVehicle() => PostManifest();

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

    [When("the administrator erases the vehicle's driver")]
    public async Task WhenTheAdministratorErasesTheVehiclesDriver()
    {
        var admin = await api.TokenForAsync("admin");
        await api.SendAsync(HttpMethod.Delete, $"/people/{state.Pinned(CrewKey)}", admin, null);
    }

    [Then("the vehicle's crew on that convoy shows a former volunteer")]
    public async Task ThenTheVehiclesCrewShowsAFormerVolunteer()
    {
        var response = await api.SendAsync(
            HttpMethod.Get,
            $"/convoys/{state.Pinned(ConvoyKey)}/vehicles/{state.Pinned(VehicleKey)}/crew",
            state.CurrentToken,
            null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the body was: {0}", api.LastBody);
        var member = JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray().Single();
        member.GetProperty("personId").GetString().Should().Be(state.Pinned(CrewKey));
        $"{member.GetProperty("firstName").GetString()} {member.GetProperty("lastName").GetString()}"
            .Should().Be("Former volunteer");
    }

    [Given("a convoy exists whose truck list is not published")]
    public Task GivenAConvoyExistsWhoseTruckListIsNotPublished() => CreateConvoy();

    [When("I POST a manifest on the remembered convoy")]
    public Task WhenIPostAManifestOnTheRememberedConvoy() => PostManifest();

    /// <summary>
    /// The invariant the composite foreign key exists for. A manifest names a truck-list entry,
    /// so a vehicle that is on no convoy has nothing for one to hang off.
    /// </summary>
    [When("I POST a manifest for a vehicle that is not on the convoy")]
    public async Task WhenIPostAManifestForAVehicleThatIsNotOnTheConvoy()
    {
        var operatorToken = await api.TokenForAsync("operator");
        var vin = "BDD" + Guid.NewGuid().ToString("N")[..14].ToUpperInvariant();

        (await api.SendAsync(HttpMethod.Post, "/vehicles", operatorToken, $$"""
            { "vin": "{{vin}}", "plate": "UA20ACT", "year": 2015, "fuel": "Diesel", "transmission": "Manual", "weightKg": 2100 }
            """)).StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);
        state.CreatedResources.Add(("vehicles", vin));

        await api.SendAsync(
            HttpMethod.Post,
            $"/convoys/{state.Pinned(ConvoyKey)}/vehicles/{vin}/manifest",
            state.CurrentToken,
            $$"""{ "id": "{{state.Pinned(ManifestKey)}}" }""");
    }

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

    /// <summary>
    /// Opens a manifest against the remembered convoy's vehicle. There is no <c>POST /manifests</c>
    /// any more: a manifest is the paperwork for one vehicle on one convoy, so it is created on
    /// the truck-list entry and only the document reference is in the body.
    /// </summary>
    private async Task PostManifest()
    {
        var response = await api.SendAsync(
            HttpMethod.Post,
            $"/convoys/{state.Pinned(ConvoyKey)}/vehicles/{state.Pinned(VehicleKey)}/manifest",
            state.CurrentToken,
            $$"""{ "id": "{{state.Pinned(ManifestKey)}}" }""");

        if (response.StatusCode == HttpStatusCode.Created)
        {
            state.CreatedResources.Add(("manifests", state.Pinned(ManifestKey)));
        }
    }

    /// <summary>
    /// A convoy with one inspected vehicle on its truck list, pinned as the manifest's vehicle.
    /// The vehicle is part of the arrangement rather than an extra step, because a manifest is
    /// opened against a truck-list entry and there is no longer any way to open one without.
    /// </summary>
    /// <param name="withVehicle">
    /// False for arrangements that provision their own vehicle. A convoy carrying a second truck
    /// nobody wrote a manifest for can never arrive, which is the point of the arrival rule.
    /// </param>
    private async Task CreateConvoy(bool withVehicle = true)
    {
        var response = await api.SendAsync(HttpMethod.Post, "/convoys", state.CurrentToken, ConvoyBody);

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);

        var location = response.Headers.Location!;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.ToString();
        var convoyId = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];

        state.CreatedResources.Add(("convoys", convoyId));
        state.Pin(ConvoyKey, convoyId);

        if (!withVehicle)
        {
            return;
        }

        var operatorToken = await api.TokenForAsync("operator");
        var vin = "BDD" + Guid.NewGuid().ToString("N")[..14].ToUpperInvariant();

        (await api.SendAsync(HttpMethod.Post, "/vehicles", operatorToken, $$"""
            { "vin": "{{vin}}", "plate": "UA20ACT", "year": 2015, "fuel": "Diesel", "transmission": "Manual", "weightKg": 2100 }
            """)).StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);
        state.CreatedResources.Add(("vehicles", vin));

        (await api.SendAsync(HttpMethod.Put, $"/vehicles/{vin}/inspection", operatorToken, """{ "status": "Passed" }"""))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "the body was: {0}", api.LastBody);
        (await api.SendAsync(HttpMethod.Put, $"/convoys/{convoyId}/vehicles/{vin}", operatorToken, null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "the body was: {0}", api.LastBody);

        state.Pin(VehicleKey, vin);
    }
}
