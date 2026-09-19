using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Vehicles;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// The <c>/vehicles</c> contract from the outside: status codes, the authorization split
/// between reads and writes, and validation. Persistence is faked
/// (<see cref="InMemoryVehicleRepository"/>); the Dapper repository has its own tests.
/// </summary>
public class VehicleEndpointTests
{
    private const string Vin = "WVWZZZ1JZXW000001";

    private static VehicleReadModel AStoredVehicle(string vin = Vin) => new(
        vin, "AB12CDE", "Volkswagen", "Transporter", "White",
        TransmissionType.Manual, null, 92_000, false, 2016, FuelType.Diesel,
        null, "operator", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), 1_400,
        900.50m, 150.25m, 300.00m, 180.75m);

    private static object ACreateBody(string vin = Vin) => new
    {
        vin,
        plate = "AB12CDE",
        year = 2016,
        fuel = "Diesel",
        transmission = "Manual",
        weightKg = 1_400,
    };

    [Fact]
    public async Task Listing_vehicles_without_a_token_is_rejected()
    {
        await using var api = FreedomApi.WithVehicles(new InMemoryVehicleRepository(), authenticated: false);
        using var client = api.CreateClient();

        var response = await client.GetAsync("/vehicles", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_loader_may_read_vehicles()
    {
        await using var api = FreedomApi.WithVehicles(
            new InMemoryVehicleRepository(AStoredVehicle()), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.GetAsync("/vehicles", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_loader_may_not_create_a_vehicle()
    {
        await using var api = FreedomApi.WithVehicles(new InMemoryVehicleRepository(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/vehicles", ACreateBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_purchaser_creates_a_vehicle_and_gets_its_location_back()
    {
        var repository = new InMemoryVehicleRepository();
        await using var api = FreedomApi.WithVehicles(repository, roles: "Purchaser");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/vehicles", ACreateBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().EndWith($"/vehicles/{Vin}");
        repository.Contains(Vin).Should().BeTrue();
    }

    [Fact]
    public async Task Creating_a_vehicle_whose_VIN_is_taken_is_a_conflict()
    {
        await using var api = FreedomApi.WithVehicles(
            new InMemoryVehicleRepository(AStoredVehicle()), roles: "Purchaser");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/vehicles", ACreateBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_purchaser_creates_a_vehicle_with_cargo_capacity()
    {
        var repository = new InMemoryVehicleRepository();
        await using var api = FreedomApi.WithVehicles(repository, roles: "Purchaser");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/vehicles",
            new
            {
                vin = Vin,
                plate = "AB12CDE",
                year = 2016,
                fuel = "Diesel",
                transmission = "Manual",
                weightKg = 1_400,
                maxCargoWeightKg = 900.25m,
                cargoWidthCm = 100m,
                cargoDepthCm = 200m,
                cargoHeightCm = 150m,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var stored = await repository.GetByVinAsync(Vin, CancellationToken.None);
        stored!.MaxCargoWeightKg.Should().Be(900.25m);
        stored.CargoWidthCm.Should().Be(100m);
        stored.CargoDepthCm.Should().Be(200m);
        stored.CargoHeightCm.Should().Be(150m);
    }

    [Fact]
    public async Task Creating_a_vehicle_with_a_negative_cargo_weight_is_a_validation_problem()
    {
        var repository = new InMemoryVehicleRepository();
        await using var api = FreedomApi.WithVehicles(repository, roles: "Purchaser");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/vehicles",
            new
            {
                vin = Vin,
                plate = "AB12CDE",
                year = 2016,
                fuel = "Diesel",
                transmission = "Manual",
                weightKg = 1_400,
                maxCargoWeightKg = -1,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        repository.Count.Should().Be(0);
    }

    [Fact]
    public async Task Creating_a_vehicle_with_a_blank_VIN_is_a_validation_problem()
    {
        var repository = new InMemoryVehicleRepository();
        await using var api = FreedomApi.WithVehicles(repository, roles: "Purchaser");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/vehicles",
            new { vin = "", plate = "AB12CDE", year = 2016, fuel = "Diesel", transmission = "Manual", weightKg = 1_400 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("errors").TryGetProperty("Vin", out _).Should().BeTrue();
        repository.Count.Should().Be(0);
    }

    [Fact]
    public async Task Fetching_a_known_vehicle_returns_it()
    {
        await using var api = FreedomApi.WithVehicles(
            new InMemoryVehicleRepository(AStoredVehicle()), roles: "Dispatcher");
        using var client = api.CreateClient();

        var vehicle = await client.GetFromJsonAsync<JsonElement>($"/vehicles/{Vin}", TestContext.Current.CancellationToken);

        vehicle.GetProperty("vin").GetString().Should().Be(Vin);
        vehicle.GetProperty("plate").GetString().Should().Be("AB12CDE");
    }

    [Fact]
    public async Task Fetching_an_unknown_vehicle_is_a_404()
    {
        await using var api = FreedomApi.WithVehicles(new InMemoryVehicleRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync("/vehicles/UNKNOWNVIN0000001", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_administrator_updates_a_vehicle()
    {
        var repository = new InMemoryVehicleRepository(AStoredVehicle());
        await using var api = FreedomApi.WithVehicles(repository, roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/vehicles/{Vin}",
            new { plate = "ZZ99ZZZ", year = 2016, fuel = "Diesel", transmission = "Manual", weightKg = 1_500 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var stored = await repository.GetByVinAsync(Vin, CancellationToken.None);
        stored!.Plate.Should().Be("ZZ99ZZZ");
        stored.WeightKg.Should().Be(1_500);
    }

    [Fact]
    public async Task Updating_an_unknown_vehicle_is_a_404()
    {
        await using var api = FreedomApi.WithVehicles(new InMemoryVehicleRepository(), roles: "Purchaser");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/vehicles/UNKNOWNVIN0000001",
            new { plate = "ZZ99ZZZ", year = 2016, fuel = "Diesel", transmission = "Manual", weightKg = 1_500 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_purchaser_deletes_a_vehicle()
    {
        var repository = new InMemoryVehicleRepository(AStoredVehicle());
        await using var api = FreedomApi.WithVehicles(repository, roles: "Purchaser");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/vehicles/{Vin}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        repository.Contains(Vin).Should().BeFalse();
    }

    [Fact]
    public async Task A_new_vehicle_reads_back_as_awaiting_inspection()
    {
        await using var api = FreedomApi.WithVehicles(
            new InMemoryVehicleRepository(AStoredVehicle()), roles: "Dispatcher");
        using var client = api.CreateClient();

        var vehicle = await client.GetFromJsonAsync<JsonElement>($"/vehicles/{Vin}", TestContext.Current.CancellationToken);

        vehicle.GetProperty("inspectionStatus").GetString().Should().Be("Pending");
        vehicle.GetProperty("inspectionNotes").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("Mechanic")]
    [InlineData("Administrator")]
    public async Task A_recorded_inspection_is_read_back(string role)
    {
        await using var api = FreedomApi.WithVehicles(new InMemoryVehicleRepository(AStoredVehicle()), roles: role);
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/vehicles/{Vin}/inspection",
            new { status = "Failed", notes = "Nearside rear tyre below legal tread" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var vehicle = await client.GetFromJsonAsync<JsonElement>($"/vehicles/{Vin}", TestContext.Current.CancellationToken);
        vehicle.GetProperty("inspectionStatus").GetString().Should().Be("Failed");
        vehicle.GetProperty("inspectionNotes").GetString().Should().Be("Nearside rear tyre below legal tread");
    }

    [Theory]
    [InlineData("Purchaser")]
    [InlineData("Dispatcher")]
    [InlineData("Loader")]
    [InlineData("GroundOfficer")]
    public async Task Only_a_mechanic_or_administrator_may_record_an_inspection(string role)
    {
        var repository = new InMemoryVehicleRepository(AStoredVehicle());
        await using var api = FreedomApi.WithVehicles(repository, roles: role);
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/vehicles/{Vin}/inspection", new { status = "Passed" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await repository.GetByVinAsync(Vin, CancellationToken.None))!.InspectionStatus.Should().Be(InspectionStatus.Pending);
    }

    [Fact]
    public async Task Recording_an_inspection_for_an_unknown_vehicle_is_a_404()
    {
        await using var api = FreedomApi.WithVehicles(new InMemoryVehicleRepository(), roles: "Mechanic");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/vehicles/UNKNOWNVIN0000001/inspection", new { status = "Passed" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Inspection_notes_longer_than_the_column_are_a_validation_problem()
    {
        var repository = new InMemoryVehicleRepository(AStoredVehicle());
        await using var api = FreedomApi.WithVehicles(repository, roles: "Mechanic");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/vehicles/{Vin}/inspection",
            new { status = "Failed", notes = new string('x', 2_001) },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("errors").TryGetProperty("Notes", out _).Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_inspection_status_is_rejected()
    {
        await using var api = FreedomApi.WithVehicles(new InMemoryVehicleRepository(AStoredVehicle()), roles: "Mechanic");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/vehicles/{Vin}/inspection", new { status = "Roadworthy" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Editing_a_vehicle_leaves_its_inspection_alone()
    {
        var repository = new InMemoryVehicleRepository(
            AStoredVehicle() with { InspectionStatus = InspectionStatus.Passed, InspectionNotes = "Ready" });
        await using var api = FreedomApi.WithVehicles(repository, roles: "Mechanic");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/vehicles/{Vin}",
            new
            {
                plate = "ZZ99ZZZ", year = 2016, fuel = "Diesel", transmission = "Manual", weightKg = 1_500,
                inspectionStatus = "Failed", inspectionNotes = "forged",
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var vehicle = await client.GetFromJsonAsync<JsonElement>($"/vehicles/{Vin}", TestContext.Current.CancellationToken);
        vehicle.GetProperty("plate").GetString().Should().Be("ZZ99ZZZ");
        vehicle.GetProperty("inspectionStatus").GetString().Should().Be("Passed");
        vehicle.GetProperty("inspectionNotes").GetString().Should().Be("Ready");
    }

    [Fact]
    public async Task Editing_a_vehicle_cannot_move_it_on_or_off_a_convoy()
    {
        var repository = new InMemoryVehicleRepository(AStoredVehicle() with { ConvoyId = 7 });
        await using var api = FreedomApi.WithVehicles(repository, roles: "Purchaser");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/vehicles/{Vin}",
            new { plate = "ZZ99ZZZ", year = 2016, fuel = "Diesel", transmission = "Manual", weightKg = 1_500, convoyId = 9 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var vehicle = await client.GetFromJsonAsync<JsonElement>($"/vehicles/{Vin}", TestContext.Current.CancellationToken);
        vehicle.GetProperty("convoyId").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task A_new_vehicle_is_on_no_convoy_whatever_the_body_says()
    {
        var repository = new InMemoryVehicleRepository();
        await using var api = FreedomApi.WithVehicles(repository, roles: "Purchaser");
        using var client = api.CreateClient();

        await client.PostAsJsonAsync(
            "/vehicles",
            new { vin = Vin, plate = "AB12CDE", year = 2016, fuel = "Diesel", transmission = "Manual", weightKg = 1_400, convoyId = 9 },
            TestContext.Current.CancellationToken);

        (await repository.GetByVinAsync(Vin, CancellationToken.None))!.ConvoyId.Should().BeNull();
    }

    [Fact]
    public async Task A_mechanic_may_add_a_vehicle()
    {
        var repository = new InMemoryVehicleRepository();
        await using var api = FreedomApi.WithVehicles(repository, roles: "Mechanic");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/vehicles", ACreateBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        repository.Contains(Vin).Should().BeTrue();
    }

    [Fact]
    public async Task Deleting_an_unknown_vehicle_is_a_404()
    {
        await using var api = FreedomApi.WithVehicles(new InMemoryVehicleRepository(), roles: "Purchaser");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync("/vehicles/UNKNOWNVIN0000001", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_vehicle_named_on_a_manifest_cannot_be_deleted()
    {
        var repository = new InMemoryVehicleRepository(AStoredVehicle()).NamedOnAManifest(Vin);
        await using var api = FreedomApi.WithVehicles(repository, roles: "Purchaser");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/vehicles/{Vin}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("detail").GetString().Should().Contain("manifest");
        repository.Contains(Vin).Should().BeTrue();
    }
}
