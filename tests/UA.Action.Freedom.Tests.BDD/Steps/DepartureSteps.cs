using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Reqnroll;
using UA.Action.Freedom.Tests.BDD.Support;

namespace UA.Action.Freedom.Tests.BDD.Steps;

/// <summary>
/// Steps for departing a convoy (O36). A convoy departs as a whole, once no blocking requirement is outstanding, and the
/// requirements are many: a route, a leader, and for each vehicle a ferry, a registered handover Receiver, its
/// boxes and its declarations. <see cref="WhenIMakeTheConvoyReadyToDepart"/> does what a Dispatcher would, tolerating
/// anything a scenario already did, so a scenario about something else can still reach a departure.
/// </summary>
[Binding]
public sealed class DepartureSteps(FreedomApiClient api, ScenarioState state)
{
    private const string ConvoyKey = "convoy-for-manifest";
    private const string VehicleKey = "vehicle-for-manifest";
    private const string CrewKey = "crew-for-manifest";

    /// <summary>The registered Receiver a scenario names, as a box's destination and as the vehicle's handover.</summary>
    internal const string ReceiverKey = "departure-receiver";

    private string ConvoyPath => $"/convoys/{state.Pinned(ConvoyKey)}";

    private string VehiclePath => $"{ConvoyPath}/vehicles/{state.Pinned(VehicleKey)}";

    [Given("a registered receiver exists")]
    public Task GivenARegisteredReceiverExists() => EnsureReceiverAsync();

    [When("I make the convoy of the insured vehicle ready to depart")]
    public async Task WhenIMakeTheConvoyReadyToDepart()
    {
        var admin = await api.TokenForAsync("admin");
        var receiver = await EnsureReceiverAsync();

        await DoAsync(HttpMethod.Put, $"{ConvoyPath}/route", admin, """
            { "stops": [
                { "name": "Coventry depot", "kind": "Hub", "house": "Unit 4", "street": "Cross Road", "city": "Coventry", "country": "United Kingdom", "postcode": "CV1 2AB" },
                { "name": "Warsaw hub", "street": "Trasa Katowicka", "city": "Warszawa", "country": "Poland", "postcode": "80-180" }
            ] }
            """);
        await DoAsync(HttpMethod.Put, $"{ConvoyPath}/leader", admin, $$"""{ "personId": "{{state.Pinned(CrewKey)}}" }""");
        await DoAsync(HttpMethod.Put, $"{VehiclePath}/ferry", admin, """
            { "operator": "P&O Ferries", "reference": "BDD-FERRY-1", "sailingAt": "2026-09-02T07:30:00Z", "ticketDetails": "Freight" }
            """);
        await DoAsync(HttpMethod.Put, $"{VehiclePath}/handover-receiver", admin, $$"""{ "receiverRef": "{{receiver}}" }""");

        await DoAsync(HttpMethod.Put, $"{VehiclePath}/declarations/ens", admin, $$"""
            { "mrn": "26FR{{Guid.NewGuid().ToString("N").ToUpperInvariant()[..14]}}", "acceptedAt": "2026-08-24T09:30:00+00:00", "filedBy": "groundofficer", "filingReference": "STP-BDD" }
            """);
        await DoAsync(HttpMethod.Post, $"{VehiclePath}/declarations/gmr/record", admin, """{ "reference": "GMR-BDD-DEP" }""");
        await DoAsync(HttpMethod.Post, $"{VehiclePath}/declarations/elo/record", admin, """{ "reference": "ELO-BDD-DEP" }""");

        foreach (var receiverRef in await ReceiversOfTheLoadAsync(admin))
        {
            await DoAsync(
                HttpMethod.Post,
                $"{VehiclePath}/declarations/goods-list/record",
                admin,
                $$"""{ "reference": "UA-BDD-DEP", "receiverRef": "{{receiverRef}}" }""");
        }
    }

    [When("I depart the convoy of the insured vehicle")]
    public Task WhenIDepartTheConvoy() =>
        api.SendAsync(HttpMethod.Post, $"{ConvoyPath}/depart", state.CurrentToken, null);

    [When("I POST \"(.*)\" on the convoy of the insured vehicle")]
    public Task WhenIPostOnTheConvoy(string suffix) =>
        api.SendAsync(HttpMethod.Post, ConvoyPath + suffix, state.CurrentToken, null);

    [Then("the departure is refused because \"(.*)\" is outstanding")]
    public void ThenTheDepartureIsRefusedBecause(string code)
    {
        ((int)api.LastResponse!.StatusCode).Should().Be(409, "the body was: {0}", api.LastBody);
        OutstandingCodes().Should().Contain(code, "the body was: {0}", api.LastBody);
    }

    [Then("the departure is not refused because of \"(.*)\"")]
    public void ThenTheDepartureIsNotRefusedBecause(string code) =>
        OutstandingCodes().Should().NotContain(code, "the body was: {0}", api.LastBody);

    [Then("the convoy of the insured vehicle has departed")]
    public async Task ThenTheConvoyHasDeparted()
    {
        var response = await api.SendAsync(HttpMethod.Get, ConvoyPath, state.CurrentToken, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(api.LastBody).RootElement
            .GetProperty("departedAt").ValueKind.Should().Be(JsonValueKind.String, "the body was: {0}", api.LastBody);
    }

    private IReadOnlyList<string> OutstandingCodes() =>
    [
        .. JsonDocument.Parse(api.LastBody).RootElement.GetProperty("requirements").EnumerateArray()
            .Select(requirement => requirement.GetProperty("code").GetString()!),
    ];

    private async Task<string> EnsureReceiverAsync()
    {
        if (state.TryPinned(ReceiverKey) is { } existing)
        {
            return existing;
        }

        var groundOfficer = await api.TokenForAsync("groundofficer");
        var created = await api.SendAsync(HttpMethod.Post, "/receivers", groundOfficer, """
            { "organisation": "Kharkiv Regional Hospital", "region": "Kharkiv oblast" }
            """);
        created.StatusCode.Should().Be(HttpStatusCode.Created, "the body was: {0}", api.LastBody);
        var location = created.Headers.Location!;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.ToString();
        var receiverRef = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];
        state.CreatedResources.Add(("receivers", receiverRef));

        var admin = await api.TokenForAsync("admin");
        (await api.SendAsync(HttpMethod.Put, $"/receivers/{receiverRef}/status", admin, """{ "status": "Registered" }"""))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "the body was: {0}", api.LastBody);

        state.Pin(ReceiverKey, receiverRef);
        return receiverRef;
    }

    /// <summary>The distinct Receivers named by the boxes allocated to the vehicle: one goods list each.</summary>
    private async Task<IReadOnlyList<string>> ReceiversOfTheLoadAsync(string token)
    {
        var list = await api.SendAsync(HttpMethod.Get, $"{VehiclePath}/boxes", token, null);
        list.StatusCode.Should().Be(HttpStatusCode.OK, "the body was: {0}", api.LastBody);
        var boxIds = JsonDocument.Parse(api.LastBody).RootElement.EnumerateArray()
            .Select(box => box.GetProperty("boxId").GetInt32())
            .ToList();

        var receivers = new List<string>();
        foreach (var boxId in boxIds)
        {
            await api.SendAsync(HttpMethod.Get, $"/boxes/{boxId}", token, null);
            var receiver = JsonDocument.Parse(api.LastBody).RootElement.GetProperty("receiverRef");
            if (receiver.ValueKind == JsonValueKind.String && !receivers.Contains(receiver.GetString()!))
            {
                receivers.Add(receiver.GetString()!);
            }
        }

        return receivers;
    }

    /// <summary>One setup call. A scenario may have done it already, so a conflict is fine; anything else is a bug.</summary>
    private async Task DoAsync(HttpMethod method, string path, string token, string body)
    {
        var response = await api.SendAsync(method, path, token, body);

        ((int)response.StatusCode).Should().BeOneOf(
            [200, 201, 202, 204, 409], "{0} {1} said: {2}", method, path, api.LastBody);
    }
}
