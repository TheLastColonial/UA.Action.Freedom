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
        null, "operator", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), 1_400);

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
    public async Task Deleting_an_unknown_vehicle_is_a_404()
    {
        await using var api = FreedomApi.WithVehicles(new InMemoryVehicleRepository(), roles: "Purchaser");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync("/vehicles/UNKNOWNVIN0000001", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
