using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Application.Vehicles;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// O10 and O25: the Dispatcher closes a convoy after it has arrived, the report is the account of it, and it can be drawn
/// again after a correction because closing does not lock. The report carries no address and no delivery detail.
/// </summary>
public class ConvoyClosingEndpointTests
{
    private const int ConvoyId = 42;
    private const string Vin = "WVWZZZ1JZXW000001";
    private const string ManifestId = "MAN-0001";
    private const int Delivered = 1;
    private const int Seized = 2;
    private const int Returned = 3;
    private const int Hub = 10;
    private static readonly Guid Hospital = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Clinic = new("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Published = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);

    private static BoxItemReadModel Worth(decimal value) =>
        new(Guid.NewGuid(), "Bandages", new Dictionary<string, string>(), 3, Quantity: 1, ValueGbp: value);

    private static WebApplicationFactory<Program> AConvoy(params string[] roles)
    {
        var convoys = new InMemoryConvoyRepository(
                new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), Published, DepartedAt: Departs))
            .WithVehicle(Vin, onConvoy: ConvoyId)
            .WithKnownBox(new ManifestBoxReadModel(Delivered, 12, Validated: true))
            .WithKnownBox(new ManifestBoxReadModel(Seized, 12, Validated: true))
            .WithKnownBox(new ManifestBoxReadModel(Returned, 12, Validated: true));
        var receivers = new InMemoryReceiverRepository(
            new ReceiverReadModel(Hospital, "Lviv Hospital 4", "Lviv", ReceiverStatus.Registered),
            new ReceiverReadModel(Clinic, "Kyiv Clinic", "Kyiv", ReceiverStatus.Registered));
        var boxes = new InMemoryBoxRepository(
                new BoxReadModel(Delivered, 12, null, null, null, Hospital, null, null, null),
                new BoxReadModel(Seized, 12, null, null, null, Hospital, null, null, null),
                new BoxReadModel(Returned, 12, null, null, null, Clinic, null, null, null))
            .WithItem(Delivered, Worth(100m))
            .WithItem(Seized, Worth(400m))
            .WithItem(Returned, Worth(30m));
        var locations = new InMemoryLocationRepository(
            new LocationReadModel(Hub, "Coventry hub", null, null, null, null, null, IsRegisteredHub: true));
        var manifests = new InMemoryManifestRepository(
            new ManifestReadModel(ManifestId, ConvoyId, Vin, ManifestStatus.Approved, null));
        var vehicles = new InMemoryVehicleRepository(
            new VehicleReadModel(
                Vin, "AB12CDE", null, null, null, TransmissionType.Manual, null, null, false, 2016, FuelType.Diesel,
                null, null, null, 1_400, null, null, null, null, ValueGbp: 4_000m, ValueSource: ValueSource.Estimate));

        return FreedomApi.WithManifests(
            manifests, convoys, new InMemoryPersonRepository().CalledByALinkedVolunteer(), new RecordingManifestWorkQueue(),
            boxes: boxes, receivers: receivers, locations: locations, vehicles: vehicles, roles: roles);
    }

    private static async Task<HttpClient> AnArrivedConvoyAsync(WebApplicationFactory<Program> api)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = api.CreateClient();

        foreach (var boxId in new[] { Delivered, Seized, Returned })
        {
            (await client.PutAsync($"/convoys/{ConvoyId}/vehicles/{Vin}/boxes/{boxId}", null, ct))
                .IsSuccessStatusCode.Should().BeTrue();
        }

        (await client.PostAsync($"/convoys/{ConvoyId}/vehicles/{Vin}/delivered", null, ct)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/convoys/{ConvoyId}/boxes/{Seized}/outcome", new { outcome = "Seized" }, ct))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(
            $"/convoys/{ConvoyId}/boxes/{Returned}/outcome", new { outcome = "ReturnedToHub", hubLocationId = Hub }, ct))
            .EnsureSuccessStatusCode();
        (await client.PostAsync($"/convoys/{ConvoyId}/arrive", null, ct)).EnsureSuccessStatusCode();
        return client;
    }

    private static Task<HttpResponseMessage> Close(HttpClient client) =>
        client.PostAsync($"/convoys/{ConvoyId}/close", null, TestContext.Current.CancellationToken);

    private static Task<JsonElement> ReportAsync(HttpClient client) =>
        client.GetFromJsonAsync<JsonElement>($"/convoys/{ConvoyId}/report", TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_convoy_cannot_be_closed_before_it_has_arrived()
    {
        await using var api = AConvoy("Dispatcher");
        using var client = api.CreateClient();

        var response = await Close(client);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("type").GetString().Should().Be("convoy-not-arrived");
    }

    [Fact]
    public async Task Closing_an_arrived_convoy_stamps_it_and_returns_the_report()
    {
        await using var api = AConvoy("Dispatcher");
        using var client = await AnArrivedConvoyAsync(api);

        var response = await Close(client);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        report.GetProperty("closedAt").ValueKind.Should().Be(JsonValueKind.String);
        var convoy = await client.GetFromJsonAsync<JsonElement>($"/convoys/{ConvoyId}", TestContext.Current.CancellationToken);
        convoy.GetProperty("closedAt").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task The_report_counts_delivered_value_without_the_seized_or_returned_boxes()
    {
        await using var api = AConvoy("Dispatcher");
        using var client = await AnArrivedConvoyAsync(api);

        var report = await ReportAsync(client);

        var totals = report.GetProperty("totals");
        totals.GetProperty("boxes").GetInt32().Should().Be(3);
        totals.GetProperty("boxesDelivered").GetInt32().Should().Be(1);
        totals.GetProperty("boxesNotDelivered").GetInt32().Should().Be(2);
        totals.GetProperty("deliveredValueGbp").GetDecimal().Should().Be(100m);
        totals.GetProperty("vehicleValueGbp").GetDecimal().Should().Be(4_000m);
        var vehicle = report.GetProperty("vehicles").EnumerateArray().Single();
        vehicle.GetProperty("byOutcome").EnumerateArray()
            .Single(o => o.GetProperty("outcome").GetString() == "Seized").GetProperty("valueGbp").GetDecimal().Should().Be(400m);
    }

    [Fact]
    public async Task The_report_names_receivers_at_organisation_and_region_only()
    {
        await using var api = AConvoy("Dispatcher");
        using var client = await AnArrivedConvoyAsync(api);

        var text = (await ReportAsync(client)).GetRawText();

        text.Should().Contain("Lviv Hospital 4").And.Contain("Kyiv Clinic");
        text.Should().NotContain(Hospital.ToString()).And.NotContain(Clinic.ToString(), "a receiver is never identified in the report");
        var names = PropertyNames(await ReportAsync(client));
        names.Should().NotContain(name =>
            name.Contains("address") || name.Contains("street") || name.Contains("city") || name.Contains("postcode")
            || name.Contains("house") || name.Contains("phone") || name.Contains("contact") || name.Contains("email")
            || name.Contains("receiverref") || name.Contains("delivery"));
    }

    [Fact]
    public async Task A_correction_after_closing_changes_the_regenerated_report_and_closing_again_is_allowed()
    {
        await using var api = AConvoy("Dispatcher");
        using var client = await AnArrivedConvoyAsync(api);
        (await Close(client)).EnsureSuccessStatusCode();
        (await ReportAsync(client)).GetProperty("totals").GetProperty("boxesDelivered").GetInt32().Should().Be(1);

        (await client.PostAsJsonAsync(
            $"/convoys/{ConvoyId}/boxes/{Delivered}/outcome", new { outcome = "Seized" }, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK, "closing does not lock the convoy");

        var corrected = await ReportAsync(client);
        corrected.GetProperty("totals").GetProperty("boxesDelivered").GetInt32().Should().Be(0);
        corrected.GetProperty("totals").GetProperty("deliveredValueGbp").GetDecimal().Should().Be(0m);
        corrected.GetProperty("closedAt").ValueKind.Should().Be(JsonValueKind.String);
        (await Close(client)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_report_can_be_read_before_the_convoy_is_closed()
    {
        await using var api = AConvoy("Dispatcher");
        using var client = await AnArrivedConvoyAsync(api);

        var report = await ReportAsync(client);

        report.GetProperty("closedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_unknown_convoy_has_no_report_and_cannot_be_closed()
    {
        await using var api = AConvoy("Dispatcher");
        using var client = api.CreateClient();

        (await client.GetAsync("/convoys/999/report", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsync("/convoys/999/close", null, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_loader_cannot_close_a_convoy()
    {
        await using var api = AConvoy("Loader");
        using var client = api.CreateClient();

        (await Close(client)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .SelectMany(property => PropertyNames(property.Value).Prepend(property.Name.ToLowerInvariant())),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };
}
