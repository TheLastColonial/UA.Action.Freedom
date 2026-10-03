using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Application.Vehicles;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// ADR 0017 from the outside: an entity says who last changed it and when, the person comes from
/// the login and never from the request, a login nobody has linked cannot write, and a person who
/// has been erased reads as "Former volunteer". Persistence is faked; the Dapper repositories prove
/// the stamp reaches the row in the integration tests.
/// </summary>
public class LastChangedEndpointTests
{
    private const string CallerName = "Test User";
    private const string Vin = "WVWZZZ1JZXW000001";

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    private static void ShouldBeChangedByTheCaller(JsonElement entity)
    {
        entity.GetProperty("lastChangedByName").GetString().Should().Be(CallerName);
        entity.GetProperty("lastChangedAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    private static object AVehicleBody() => new
    {
        vin = Vin,
        plate = "AB12CDE",
        year = 2016,
        fuel = "Diesel",
        transmission = "Manual",
        weightKg = 1_400,
    };

    [Fact]
    public async Task A_vehicle_a_purchaser_creates_says_who_last_changed_it_and_when()
    {
        await using var api = FreedomApi.WithVehicles(new InMemoryVehicleRepository(), roles: "Purchaser");
        using var client = api.CreateClient();

        await client.PostAsJsonAsync("/vehicles", AVehicleBody(), TestContext.Current.CancellationToken);
        var vehicle = await JsonOf(await client.GetAsync($"/vehicles/{Vin}", TestContext.Current.CancellationToken));

        ShouldBeChangedByTheCaller(vehicle);
    }

    [Fact]
    public async Task Recording_an_inspection_is_a_change_signed_by_the_mechanic()
    {
        var repository = new InMemoryVehicleRepository();
        await using var api = FreedomApi.WithVehicles(repository, roles: ["Administrator", "Mechanic"]);
        using var client = api.CreateClient();
        await client.PostAsJsonAsync("/vehicles", AVehicleBody(), TestContext.Current.CancellationToken);

        await client.PutAsJsonAsync(
            $"/vehicles/{Vin}/inspection",
            new { status = "Passed", notes = "Brakes done" },
            TestContext.Current.CancellationToken);
        var vehicle = await JsonOf(await client.GetAsync($"/vehicles/{Vin}", TestContext.Current.CancellationToken));

        vehicle.GetProperty("inspectionStatus").GetString().Should().Be("Passed");
        ShouldBeChangedByTheCaller(vehicle);
    }

    [Fact]
    public async Task A_vehicle_nobody_has_changed_since_it_was_seeded_has_no_last_change()
    {
        var seeded = new VehicleReadModel(
            Vin, "AB12CDE", null, null, null, TransmissionType.Manual, null, null, false, 2016, FuelType.Diesel,
            null, null, null, 1_400, null, null, null, null);
        await using var api = FreedomApi.WithVehicles(new InMemoryVehicleRepository(seeded), roles: "Loader");
        using var client = api.CreateClient();

        var vehicle = await JsonOf(await client.GetAsync($"/vehicles/{Vin}", TestContext.Current.CancellationToken));

        vehicle.GetProperty("lastChangedByName").ValueKind.Should().Be(JsonValueKind.Null);
        vehicle.GetProperty("lastChangedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_convoy_a_dispatcher_plans_says_who_last_changed_it()
    {
        await using var api = FreedomApi.WithConvoys(new InMemoryConvoyRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        await client.PostAsJsonAsync(
            "/convoys",
            new { start = "2026-09-01T06:00:00Z", expectedEnd = "2026-09-05T18:00:00Z" },
            TestContext.Current.CancellationToken);
        var convoy = await JsonOf(await client.GetAsync("/convoys/1", TestContext.Current.CancellationToken));

        ShouldBeChangedByTheCaller(convoy);
    }

    [Fact]
    public async Task A_location_an_administrator_creates_says_who_last_changed_it()
    {
        await using var api = FreedomApi.WithLocations(
            new InMemoryLocationRepository(), new InMemoryBayRepository(), roles: "Administrator");
        using var client = api.CreateClient();

        await client.PostAsJsonAsync("/locations", new { name = "Coventry Depot" }, TestContext.Current.CancellationToken);
        var location = await JsonOf(await client.GetAsync("/locations/1", TestContext.Current.CancellationToken));

        ShouldBeChangedByTheCaller(location);
    }

    [Fact]
    public async Task A_receiver_a_ground_officer_registers_says_who_last_changed_it()
    {
        var receivers = new InMemoryReceiverRepository();
        await using var api = FreedomApi.WithReceivers(
            receivers, new InMemoryReceiverDetailRepository(), roles: "GroundOfficer");
        using var client = api.CreateClient();

        var created = await client.PostAsJsonAsync(
            "/receivers",
            new { organisation = "Kharkiv Regional Hospital", region = "Kharkiv oblast" },
            TestContext.Current.CancellationToken);
        var receiver = await JsonOf(await client.GetAsync(created.Headers.Location, TestContext.Current.CancellationToken));

        ShouldBeChangedByTheCaller(receiver);
    }

    [Fact]
    public async Task A_box_a_loader_creates_says_who_last_changed_it()
    {
        await using var api = FreedomApi.WithBoxes(
            new InMemoryBoxRepository(), InMemoryPersonRepository.WithLinkedTestUser(), roles: "Loader");
        using var client = api.CreateClient();

        await client.PostAsJsonAsync("/boxes", new { }, TestContext.Current.CancellationToken);
        var box = await JsonOf(await client.GetAsync("/boxes/1", TestContext.Current.CancellationToken));

        ShouldBeChangedByTheCaller(box);
    }

    [Fact]
    public async Task A_manifest_a_dispatcher_edits_says_who_last_changed_it()
    {
        var manifest = new ManifestReadModel(
            "MAN-0001", 42, Vin, ManifestStatus.Created, null, FerryBookingComplete: false, GmrSubmittedAt: null);
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(manifest),
            new InMemoryConvoyRepository(),
            InMemoryPersonRepository.WithLinkedTestUser(),
            new RecordingManifestWorkQueue(),
            roles: "Dispatcher");
        using var client = api.CreateClient();

        await client.PutAsJsonAsync(
            "/manifests/MAN-0001",
            new { deliveryNotes = "Leave at the gate", ferryBookingComplete = true },
            TestContext.Current.CancellationToken);
        var stored = await JsonOf(await client.GetAsync("/manifests/MAN-0001", TestContext.Current.CancellationToken));

        stored.GetProperty("deliveryNotes").GetString().Should().Be("Leave at the gate");
        ShouldBeChangedByTheCaller(stored);
    }

    [Fact]
    public async Task A_volunteer_an_administrator_edits_says_who_last_changed_it()
    {
        var volunteer = new PersonReadModel(
            Guid.NewGuid(), "Olena", "Shevchenko", new DateTime(1988, 4, 12, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 2, 24, 0, 0, 0, DateTimeKind.Utc), null, false, false);
        var roster = InMemoryPersonRepository.WithLinkedTestUser(volunteer);
        await using var api = FreedomApi.WithPeople(roster, roles: "Administrator");
        using var client = api.CreateClient();

        await client.PutAsJsonAsync(
            $"/people/{volunteer.Id}",
            new
            {
                firstName = "Olena",
                lastName = "Shevchenko-Bell",
                dateOfBirth = "1988-04-12T00:00:00Z",
                joined = "2024-02-24T00:00:00Z",
                isDriver = false,
                committed = false,
            },
            TestContext.Current.CancellationToken);
        var stored = await JsonOf(await client.GetAsync($"/people/{volunteer.Id}", TestContext.Current.CancellationToken));

        ShouldBeChangedByTheCaller(stored);
    }

    [Fact]
    public async Task The_person_who_made_the_change_comes_from_the_login_not_from_the_request()
    {
        await using var api = FreedomApi.WithVehicles(new InMemoryVehicleRepository(), roles: "Purchaser");
        using var client = api.CreateClient();

        await client.PostAsJsonAsync(
            "/vehicles",
            new
            {
                vin = Vin,
                plate = "AB12CDE",
                year = 2016,
                fuel = "Diesel",
                transmission = "Manual",
                weightKg = 1_400,
                lastChangedByName = "Somebody Else",
                lastChangedAt = "2001-01-01T00:00:00Z",
            },
            TestContext.Current.CancellationToken);
        var vehicle = await JsonOf(await client.GetAsync($"/vehicles/{Vin}", TestContext.Current.CancellationToken));

        ShouldBeChangedByTheCaller(vehicle);
    }

    [Fact]
    public async Task A_person_who_has_since_been_erased_reads_as_a_former_volunteer()
    {
        var roster = InMemoryPersonRepository.WithLinkedTestUser();
        await using var api = FreedomApi.WithConvoys(new InMemoryConvoyRepository(), roster, roles: "Dispatcher");
        using var client = api.CreateClient();
        await client.PostAsJsonAsync(
            "/convoys",
            new { start = "2026-09-01T06:00:00Z", expectedEnd = "2026-09-05T18:00:00Z" },
            TestContext.Current.CancellationToken);

        await roster.DeleteAsync(InMemoryPersonRepository.TestUserId, TestContext.Current.CancellationToken);
        var convoy = await JsonOf(await client.GetAsync("/convoys/1", TestContext.Current.CancellationToken));

        convoy.GetProperty("lastChangedByName").GetString().Should().Be("Former volunteer");
    }

    [Fact]
    public async Task A_login_nobody_has_linked_cannot_edit_a_volunteer()
    {
        await using var api = FreedomApi.WithPeople(new InMemoryPersonRepository(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/people/{Guid.NewGuid()}",
            new
            {
                firstName = "Olena",
                lastName = "Shevchenko",
                dateOfBirth = "1988-04-12T00:00:00Z",
                joined = "2024-02-24T00:00:00Z",
                isDriver = false,
                committed = false,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonOf(response)).GetProperty("type").GetString().Should().Be("login-not-linked");
    }

    [Fact]
    public async Task An_unlinked_login_is_refused_before_a_vehicle_is_written()
    {
        var vehicles = new InMemoryVehicleRepository();
        await using var api = FreedomApi.WithVehicles(
            vehicles, new InMemoryPersonRepository(), roles: "Purchaser");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/vehicles", AVehicleBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonOf(response)).GetProperty("type").GetString().Should().Be("login-not-linked");
        vehicles.Count.Should().Be(0);
    }

    [Fact]
    public async Task An_unlinked_administrator_can_still_create_the_volunteer_their_login_will_be_linked_to()
    {
        // Otherwise a new deployment has no way to link its first Administrator.
        var roster = new InMemoryPersonRepository();
        await using var api = FreedomApi.WithPeople(roster, roles: "Administrator");
        using var client = api.CreateClient();

        var created = await client.PostAsJsonAsync(
            "/people",
            new
            {
                firstName = "Olena",
                lastName = "Shevchenko",
                dateOfBirth = "1988-04-12T00:00:00Z",
                joined = "2024-02-24T00:00:00Z",
                isDriver = false,
                committed = false,
            },
            TestContext.Current.CancellationToken);

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var volunteerId = created.Headers.Location!.ToString().Split('/')[^1];
        var linked = await client.PutAsJsonAsync(
            $"/people/{volunteerId}/login", new { subject = "test-user" }, TestContext.Current.CancellationToken);

        linked.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Reading_needs_no_linked_login()
    {
        await using var api = FreedomApi.WithVehicles(
            new InMemoryVehicleRepository(), new InMemoryPersonRepository(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.GetAsync("/vehicles", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
