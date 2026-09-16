using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Locations;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// The <c>/locations</c> and <c>/locations/{id}/bays</c> contract from the outside: status
/// codes, the authorization split between reads and writes, and the per-location bay code
/// uniqueness. Persistence is faked; the Dapper repositories have their own tests.
/// </summary>
public class LocationEndpointTests
{
    private const int LocationId = 3;
    private const int BayId = 9;

    private static LocationReadModel AStoredLocation(int id = LocationId) => new(
        id, "Coventry Depot", "Unit 4", "Cross Road", "Coventry", "United Kingdom", "CV1 2AB");

    private static BayReadModel AStoredBay(int id = BayId, int locationId = LocationId) => new(id, locationId, "A1");

    private static object ACreateLocationBody() => new { name = "Coventry Depot" };

    [Fact]
    public async Task Listing_locations_without_a_token_is_rejected()
    {
        await using var api = FreedomApi.WithLocations(
            new InMemoryLocationRepository(), new InMemoryBayRepository(), authenticated: false);
        using var client = api.CreateClient();

        var response = await client.GetAsync("/locations", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_loader_may_read_locations_but_not_create_one()
    {
        await using var api = FreedomApi.WithLocations(
            new InMemoryLocationRepository(AStoredLocation()), new InMemoryBayRepository(), roles: "Loader");
        using var client = api.CreateClient();

        (await client.GetAsync("/locations", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var write = await client.PostAsJsonAsync(
            "/locations", ACreateLocationBody(), TestContext.Current.CancellationToken);
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_administrator_creates_a_location_and_gets_its_location_back()
    {
        var locations = new InMemoryLocationRepository();
        await using var api = FreedomApi.WithLocations(locations, new InMemoryBayRepository(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/locations", ACreateLocationBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().Contain("/locations/");
        locations.Location(1).Should().NotBeNull();
    }

    [Fact]
    public async Task Fetching_an_unknown_location_is_a_404()
    {
        await using var api = FreedomApi.WithLocations(
            new InMemoryLocationRepository(), new InMemoryBayRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync("/locations/999", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_administrator_adds_a_bay_to_a_location()
    {
        var bays = new InMemoryBayRepository();
        await using var api = FreedomApi.WithLocations(
            new InMemoryLocationRepository(AStoredLocation()), bays, roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/locations/{LocationId}/bays", new { code = "A1" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        bays.Bay(1)!.Code.Should().Be("A1");
    }

    [Fact]
    public async Task Adding_a_bay_to_an_unknown_location_is_a_404()
    {
        await using var api = FreedomApi.WithLocations(
            new InMemoryLocationRepository(), new InMemoryBayRepository(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/locations/999/bays", new { code = "A1" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Two_bays_at_the_same_location_cannot_share_a_code()
    {
        await using var api = FreedomApi.WithLocations(
            new InMemoryLocationRepository(AStoredLocation()),
            new InMemoryBayRepository(AStoredBay()),
            roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/locations/{LocationId}/bays", new { code = "A1" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Listing_the_bays_at_a_location_returns_them()
    {
        await using var api = FreedomApi.WithLocations(
            new InMemoryLocationRepository(AStoredLocation()),
            new InMemoryBayRepository(AStoredBay()),
            roles: "Loader");
        using var client = api.CreateClient();

        var bays = await client.GetFromJsonAsync<JsonElement>(
            $"/locations/{LocationId}/bays", TestContext.Current.CancellationToken);

        var bay = bays.EnumerateArray().Should().ContainSingle().Subject;
        bay.GetProperty("code").GetString().Should().Be("A1");
    }

    [Fact]
    public async Task An_administrator_deletes_a_bay()
    {
        var bays = new InMemoryBayRepository(AStoredBay());
        await using var api = FreedomApi.WithLocations(
            new InMemoryLocationRepository(AStoredLocation()), bays, roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync(
            $"/locations/{LocationId}/bays/{BayId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        bays.Bay(BayId).Should().BeNull();
    }
}
