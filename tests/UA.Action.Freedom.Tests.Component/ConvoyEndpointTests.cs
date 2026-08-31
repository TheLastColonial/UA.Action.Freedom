using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// The <c>/convoys</c> contract from the outside: status codes, the authorization split, and
/// the rules the truck list imposes once it is published.
/// </summary>
/// <remarks>
/// The truck-list scenarios are the ones worth reading. docs/process.puml orders the work
/// <em>Truck List Created → Truck List Published → Manifest Proposed</em>, and manifests are
/// proposed against the published set of vehicles — so publication closes that set. These tests
/// are what stop a later change quietly reopening it.
/// </remarks>
public class ConvoyEndpointTests
{
    private const int Id = 42;
    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly DateTime Start = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpectedEnd = new(2026, 9, 5, 18, 0, 0, DateTimeKind.Utc);

    private static ConvoyReadModel AConvoy(bool published = false) =>
        new(Id, Start, ExpectedEnd, published ? new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc) : null);

    private static object ACreateBody() => new
    {
        start = "2026-09-01T06:00:00Z",
        expectedEnd = "2026-09-05T18:00:00Z",
    };

    private static object ARouteBody() => new
    {
        stops = new object[]
        {
            new { house = "Unit 4", street = "Cross Road", city = "Coventry", country = "United Kingdom", postcode = "CV1 2AB" },
            new { street = "Trasa Katowicka", city = "Warszawa", country = "Poland", postcode = "80-180" },
        },
    };

    [Fact]
    public async Task Listing_convoys_without_a_token_is_unauthorized()
    {
        await using var api = FreedomApi.WithConvoys(new InMemoryConvoyRepository(), authenticated: false);
        using var client = api.CreateClient();

        var response = await client.GetAsync("/convoys", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_ground_officer_is_refused_convoy_reads()
    {
        await using var api = FreedomApi.WithConvoys(new InMemoryConvoyRepository(), roles: "GroundOfficer");
        using var client = api.CreateClient();

        var response = await client.GetAsync("/convoys", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_purchaser_may_read_convoys_but_not_plan_them()
    {
        var repository = new InMemoryConvoyRepository(AConvoy());
        await using var api = FreedomApi.WithConvoys(repository, roles: "Purchaser");
        using var client = api.CreateClient();

        (await client.GetAsync("/convoys", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var write = await client.PostAsJsonAsync("/convoys", ACreateBody(), TestContext.Current.CancellationToken);

        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_dispatcher_plans_a_convoy_and_gets_its_location_back()
    {
        var repository = new InMemoryConvoyRepository();
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/convoys", ACreateBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().EndWith("/convoys/1");
        repository.Count.Should().Be(1);
    }

    [Fact]
    public async Task A_convoy_that_arrives_before_it_departs_is_a_validation_problem()
    {
        var repository = new InMemoryConvoyRepository();
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/convoys",
            new { start = "2026-09-05T18:00:00Z", expectedEnd = "2026-09-01T06:00:00Z" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        repository.Count.Should().Be(0);
    }

    [Fact]
    public async Task Fetching_an_unknown_convoy_is_a_404()
    {
        await using var api = FreedomApi.WithConvoys(new InMemoryConvoyRepository(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync("/convoys/999", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_route_is_stored_in_the_order_it_was_sent()
    {
        var repository = new InMemoryConvoyRepository(AConvoy());
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/convoys/{Id}/route", ARouteBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var route = await client.GetFromJsonAsync<JsonElement>(
            $"/convoys/{Id}/route", TestContext.Current.CancellationToken);

        var stops = route.EnumerateArray().ToList();
        stops.Should().HaveCount(2);
        stops[0].GetProperty("sequence").GetInt32().Should().Be(1);
        stops[0].GetProperty("city").GetString().Should().Be("Coventry");
        stops[1].GetProperty("sequence").GetInt32().Should().Be(2);
        stops[1].GetProperty("city").GetString().Should().Be("Warszawa");
    }

    [Fact]
    public async Task The_route_of_an_unplanned_convoy_is_an_empty_list_not_a_404()
    {
        // "No stops yet" and "no such convoy" are different answers and a client has to be able
        // to tell them apart.
        var repository = new InMemoryConvoyRepository(AConvoy());
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var route = await client.GetFromJsonAsync<JsonElement>(
            $"/convoys/{Id}/route", TestContext.Current.CancellationToken);

        route.EnumerateArray().Should().BeEmpty();

        var missing = await client.GetAsync("/convoys/999/route", TestContext.Current.CancellationToken);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_route_stop_with_no_postcode_is_a_validation_problem()
    {
        var repository = new InMemoryConvoyRepository(AConvoy());
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/convoys/{Id}/route",
            new { stops = new object[] { new { city = "Coventry", postcode = "" } } },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        repository.RouteOf(Id).Should().BeEmpty();
    }

    [Fact]
    public async Task A_dispatcher_puts_a_vehicle_on_the_truck_list()
    {
        var repository = new InMemoryConvoyRepository(AConvoy()).WithVehicle(Vin);
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            $"/convoys/{Id}/vehicles/{Vin}", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        repository.ConvoyOf(Vin).Should().Be(Id);

        var vehicles = await client.GetFromJsonAsync<JsonElement>(
            $"/convoys/{Id}/vehicles", TestContext.Current.CancellationToken);
        vehicles.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("vin").GetString().Should().Be(Vin);
    }

    [Fact]
    public async Task Putting_an_unknown_vehicle_on_a_truck_list_is_a_404()
    {
        var repository = new InMemoryConvoyRepository(AConvoy());
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            $"/convoys/{Id}/vehicles/NOSUCHVIN", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_dispatcher_takes_a_vehicle_off_a_truck_list_that_is_still_open()
    {
        var repository = new InMemoryConvoyRepository(AConvoy()).WithVehicle(Vin, onConvoy: Id);
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync(
            $"/convoys/{Id}/vehicles/{Vin}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        repository.ConvoyOf(Vin).Should().BeNull();
    }

    [Fact]
    public async Task Publishing_the_truck_list_closes_it()
    {
        var repository = new InMemoryConvoyRepository(AConvoy()).WithVehicle(Vin, onConvoy: Id);
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var publish = await client.PostAsync(
            $"/convoys/{Id}/publish-truck-list", content: null, TestContext.Current.CancellationToken);
        publish.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var convoy = await client.GetFromJsonAsync<JsonElement>($"/convoys/{Id}", TestContext.Current.CancellationToken);
        convoy.GetProperty("truckListPublished").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Publishing_a_truck_list_twice_is_a_conflict()
    {
        var repository = new InMemoryConvoyRepository(AConvoy(published: true));
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsync(
            $"/convoys/{Id}/publish-truck-list", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_published_truck_list_will_not_take_another_vehicle()
    {
        // Manifests are proposed against the published list. A vehicle added afterwards would be
        // on the road with no manifest describing it.
        var repository = new InMemoryConvoyRepository(AConvoy(published: true)).WithVehicle(Vin);
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            $"/convoys/{Id}/vehicles/{Vin}", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        repository.ConvoyOf(Vin).Should().BeNull();
    }

    [Fact]
    public async Task A_vehicle_cannot_leave_a_published_truck_list()
    {
        // The mirror image: a manifest would go on describing a truck that is no longer coming.
        var repository = new InMemoryConvoyRepository(AConvoy(published: true)).WithVehicle(Vin, onConvoy: Id);
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync(
            $"/convoys/{Id}/vehicles/{Vin}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        repository.ConvoyOf(Vin).Should().Be(Id);
    }

    [Fact]
    public async Task An_ordinary_update_cannot_unpublish_a_truck_list()
    {
        // The publication stamp is not a field of the convoy body, so there is no way to clear
        // it by sending an update — the transition is the only route.
        var repository = new InMemoryConvoyRepository(AConvoy(published: true));
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/convoys/{Id}",
            new { start = "2026-10-01T06:00:00Z", expectedEnd = "2026-10-05T18:00:00Z" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var convoy = await client.GetFromJsonAsync<JsonElement>($"/convoys/{Id}", TestContext.Current.CancellationToken);
        convoy.GetProperty("truckListPublished").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_dispatcher_cancels_a_convoy_and_releases_its_vehicles()
    {
        var repository = new InMemoryConvoyRepository(AConvoy()).WithVehicle(Vin, onConvoy: Id);
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync($"/convoys/{Id}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Donated vehicles outlive the convoy they were going to travel on.
        repository.ConvoyOf(Vin).Should().BeNull();
    }
}
