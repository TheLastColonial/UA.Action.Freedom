using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Domain;

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

    private static readonly Guid DriverId = new("0b7e8f2a-4c1d-4e5f-9a6b-7c8d9e0f1a2b");

    private static PersonReadModel APerson(Guid id, bool isDriver = true) => new(
        id, "Olena", "Bondar", new DateTime(1985, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, isDriver, Committed: true);

    private static InMemoryConvoyRepository AConvoyWithAVehicleOnIt() =>
        new InMemoryConvoyRepository(AConvoy()).WithVehicle(Vin, onConvoy: Id).WithPerson(DriverId, "Olena", "Bondar");

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

    [Theory]
    [InlineData(InspectionStatus.Pending)]
    [InlineData(InspectionStatus.Inspecting)]
    [InlineData(InspectionStatus.Failed)]
    public async Task A_vehicle_that_has_not_passed_its_inspection_cannot_join_a_convoy(InspectionStatus inspection)
    {
        var repository = new InMemoryConvoyRepository(AConvoy()).WithVehicle(Vin, inspection: inspection);
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            $"/convoys/{Id}/vehicles/{Vin}", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("detail").GetString().Should().Contain("has not passed its servicing inspection");
        repository.ConvoyOf(Vin).Should().BeNull();
    }

    [Fact]
    public async Task A_vehicle_already_on_another_convoy_is_not_moved()
    {
        const int otherConvoy = 7;
        var repository = new InMemoryConvoyRepository(AConvoy()).WithVehicle(Vin, onConvoy: otherConvoy);
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            $"/convoys/{Id}/vehicles/{Vin}", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        repository.ConvoyOf(Vin).Should().Be(otherConvoy);
    }

    [Fact]
    public async Task A_mechanic_may_not_see_or_plan_convoys()
    {
        await using var api = FreedomApi.WithConvoys(new InMemoryConvoyRepository(), roles: "Mechanic");
        using var client = api.CreateClient();

        var read = await client.GetAsync("/convoys", TestContext.Current.CancellationToken);
        var write = await client.PostAsJsonAsync("/convoys", ACreateBody(), TestContext.Current.CancellationToken);

        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_dispatcher_crews_a_vehicle_and_every_operational_role_can_read_the_crew()
    {
        var repository = AConvoyWithAVehicleOnIt();
        await using (var dispatcher = FreedomApi.WithConvoys(repository, new InMemoryPersonRepository(APerson(DriverId)), roles: "Dispatcher"))
        {
            using var client = dispatcher.CreateClient();

            var response = await client.PutAsync(
                $"/convoys/{Id}/vehicles/{Vin}/drivers/{DriverId}", content: null, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        await using var loader = FreedomApi.WithConvoys(repository, roles: "Loader");
        using var loaderClient = loader.CreateClient();

        var crew = await loaderClient.GetFromJsonAsync<JsonElement>(
            $"/convoys/{Id}/vehicles/{Vin}/drivers", TestContext.Current.CancellationToken);
        var vehicles = await loaderClient.GetFromJsonAsync<JsonElement>(
            $"/convoys/{Id}/vehicles", TestContext.Current.CancellationToken);

        crew.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("personId").GetGuid().Should().Be(DriverId);
        vehicles.EnumerateArray().Single().GetProperty("driverCount").GetInt32().Should().Be(1);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Loader")]
    [InlineData("Purchaser")]
    [InlineData("Mechanic")]
    public async Task Only_a_dispatcher_may_crew_a_vehicle(string role)
    {
        var repository = AConvoyWithAVehicleOnIt().WithDriver(Vin, DriverId);
        await using var api = FreedomApi.WithConvoys(repository, new InMemoryPersonRepository(APerson(DriverId)), roles: role);
        using var client = api.CreateClient();

        var assign = await client.PutAsync(
            $"/convoys/{Id}/vehicles/{Vin}/drivers/{Guid.NewGuid()}", content: null, TestContext.Current.CancellationToken);
        var unassign = await client.DeleteAsync(
            $"/convoys/{Id}/vehicles/{Vin}/drivers/{DriverId}", TestContext.Current.CancellationToken);

        assign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        unassign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        repository.DriverIdsOf(Vin).Should().Equal(DriverId);
    }

    [Fact]
    public async Task Crewing_a_vehicle_that_is_not_on_the_convoy_is_a_404_not_a_conflict()
    {
        var repository = new InMemoryConvoyRepository(AConvoy()).WithVehicle(Vin).WithPerson(DriverId, "Olena", "Bondar");
        await using var api = FreedomApi.WithConvoys(repository, new InMemoryPersonRepository(APerson(DriverId)), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            $"/convoys/{Id}/vehicles/{Vin}/drivers/{DriverId}", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Crewing_a_vehicle_with_a_driver_already_on_it_is_a_conflict()
    {
        var repository = AConvoyWithAVehicleOnIt().WithDriver(Vin, DriverId);
        await using var api = FreedomApi.WithConvoys(repository, new InMemoryPersonRepository(APerson(DriverId)), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            $"/convoys/{Id}/vehicles/{Vin}/drivers/{DriverId}", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_volunteer_who_does_not_drive_may_ride_as_a_passenger()
    {
        var repository = AConvoyWithAVehicleOnIt();
        await using var api = FreedomApi.WithConvoys(
            repository, new InMemoryPersonRepository(APerson(DriverId, isDriver: false)), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/convoys/{Id}/vehicles/{Vin}/drivers/{DriverId}", new { role = "Passenger" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var crew = await client.GetFromJsonAsync<JsonElement>(
            $"/convoys/{Id}/vehicles/{Vin}/drivers", TestContext.Current.CancellationToken);
        crew.EnumerateArray().Single().GetProperty("role").GetString().Should().Be("Passenger");
        var vehicles = await client.GetFromJsonAsync<JsonElement>($"/convoys/{Id}/vehicles", TestContext.Current.CancellationToken);
        vehicles.EnumerateArray().Single().GetProperty("driverCount").GetInt32().Should().Be(0);
        vehicles.EnumerateArray().Single().GetProperty("passengerCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_person_already_crewing_another_vehicle_of_the_convoy_is_a_conflict()
    {
        const string otherVin = "WVWZZZ1JZXW000002";
        var repository = AConvoyWithAVehicleOnIt().WithVehicle(otherVin, onConvoy: Id).WithDriver(otherVin, DriverId);
        await using var api = FreedomApi.WithConvoys(repository, new InMemoryPersonRepository(APerson(DriverId)), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            $"/convoys/{Id}/vehicles/{Vin}/drivers/{DriverId}", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("detail").GetString().Should().Contain("another vehicle on this convoy");
        repository.DriverIdsOf(Vin).Should().BeEmpty();
    }

    [Fact]
    public async Task A_volunteer_who_does_not_drive_cannot_crew_a_vehicle()
    {
        var repository = AConvoyWithAVehicleOnIt();
        await using var api = FreedomApi.WithConvoys(
            repository, new InMemoryPersonRepository(APerson(DriverId, isDriver: false)), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            $"/convoys/{Id}/vehicles/{Vin}/drivers/{DriverId}", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        repository.DriverIdsOf(Vin).Should().BeEmpty();
    }

    [Fact]
    public async Task Crewing_with_an_unknown_volunteer_is_a_404()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithAVehicleOnIt(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            $"/convoys/{Id}/vehicles/{Vin}/drivers/{DriverId}", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_dispatcher_stands_a_driver_down()
    {
        var repository = AConvoyWithAVehicleOnIt().WithDriver(Vin, DriverId);
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync(
            $"/convoys/{Id}/vehicles/{Vin}/drivers/{DriverId}", TestContext.Current.CancellationToken);
        var again = await client.DeleteAsync(
            $"/convoys/{Id}/vehicles/{Vin}/drivers/{DriverId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
        repository.DriverIdsOf(Vin).Should().BeEmpty();
    }

    [Fact]
    public async Task The_crew_of_a_vehicle_not_on_the_convoy_is_a_404()
    {
        var repository = new InMemoryConvoyRepository(AConvoy()).WithVehicle(Vin);
        await using var api = FreedomApi.WithConvoys(repository, roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/convoys/{Id}/vehicles/{Vin}/drivers", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Taking_a_vehicle_off_the_truck_list_stands_its_crew_down()
    {
        var repository = AConvoyWithAVehicleOnIt().WithDriver(Vin, DriverId);
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        await client.DeleteAsync($"/convoys/{Id}/vehicles/{Vin}", TestContext.Current.CancellationToken);

        repository.DriverIdsOf(Vin).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelling_a_convoy_stands_its_crews_down()
    {
        var repository = AConvoyWithAVehicleOnIt().WithDriver(Vin, DriverId);
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        await client.DeleteAsync($"/convoys/{Id}", TestContext.Current.CancellationToken);

        repository.DriverIdsOf(Vin).Should().BeEmpty();
    }

    private static object AnInsuranceBody(string coverEnd = "2026-09-30") => new
    {
        insurer = "Ukraine Aid Mutual",
        policyNumber = "POL-1",
        coverStart = "2026-08-25",
        coverEnd,
        costGbp = 412.50m,
        recordedBy = "forged-by-the-client",
    };

    [Theory]
    [InlineData("Dispatcher")]
    [InlineData("Administrator")]
    public async Task Insurance_is_recorded_against_the_caller_and_read_back(string role)
    {
        var repository = AConvoyWithAVehicleOnIt();
        await using var api = FreedomApi.WithConvoys(repository, roles: role);
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/convoys/{Id}/vehicles/{Vin}/insurance", AnInsuranceBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var policy = await client.GetFromJsonAsync<JsonElement>(
            $"/convoys/{Id}/vehicles/{Vin}/insurance", TestContext.Current.CancellationToken);
        policy.GetProperty("policyNumber").GetString().Should().Be("POL-1");
        policy.GetProperty("costGbp").GetDecimal().Should().Be(412.50m);
        policy.GetProperty("recordedBy").GetString().Should().Be("test-user");
        policy.GetProperty("voided").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("Loader")]
    [InlineData("Purchaser")]
    [InlineData("Mechanic")]
    public async Task Only_a_dispatcher_or_administrator_records_insurance(string role)
    {
        var repository = AConvoyWithAVehicleOnIt();
        await using var api = FreedomApi.WithConvoys(repository, roles: role);
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/convoys/{Id}/vehicles/{Vin}/insurance", AnInsuranceBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        repository.InsuranceOf(Id, Vin).Should().BeNull();
    }

    [Fact]
    public async Task Cover_that_ends_before_it_starts_is_a_validation_problem()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithAVehicleOnIt(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/convoys/{Id}/vehicles/{Vin}/insurance", AnInsuranceBody(coverEnd: "2026-08-01"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("errors").TryGetProperty("CoverEnd", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Insuring_a_vehicle_that_is_not_on_the_convoy_is_a_404()
    {
        await using var api = FreedomApi.WithConvoys(
            new InMemoryConvoyRepository(AConvoy()).WithVehicle(Vin), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/convoys/{Id}/vehicles/{Vin}/insurance", AnInsuranceBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_vehicle_with_no_insurance_recorded_reads_as_a_404()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithAVehicleOnIt(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/convoys/{Id}/vehicles/{Vin}/insurance", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Changing_the_crew_after_insuring_voids_the_insurance()
    {
        var repository = AConvoyWithAVehicleOnIt();
        await using var api = FreedomApi.WithConvoys(repository, new InMemoryPersonRepository(APerson(DriverId)), roles: "Dispatcher");
        using var client = api.CreateClient();
        await client.PutAsJsonAsync($"/convoys/{Id}/vehicles/{Vin}/insurance", AnInsuranceBody(), TestContext.Current.CancellationToken);

        await client.PutAsync($"/convoys/{Id}/vehicles/{Vin}/drivers/{DriverId}", content: null, TestContext.Current.CancellationToken);

        var policy = await client.GetFromJsonAsync<JsonElement>(
            $"/convoys/{Id}/vehicles/{Vin}/insurance", TestContext.Current.CancellationToken);
        policy.GetProperty("voided").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Insurance_can_be_removed()
    {
        var repository = AConvoyWithAVehicleOnIt();
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();
        await client.PutAsJsonAsync($"/convoys/{Id}/vehicles/{Vin}/insurance", AnInsuranceBody(), TestContext.Current.CancellationToken);

        var removed = await client.DeleteAsync($"/convoys/{Id}/vehicles/{Vin}/insurance", TestContext.Current.CancellationToken);
        var again = await client.DeleteAsync($"/convoys/{Id}/vehicles/{Vin}/insurance", TestContext.Current.CancellationToken);

        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static ConvoyReadModel AnArrivedConvoy() =>
        AConvoy(published: true) with { ArrivedAt = new DateTime(2026, 9, 5, 17, 0, 0, DateTimeKind.Utc) };

    [Fact]
    public async Task A_dispatcher_marks_a_convoy_arrived_and_its_delivered_vehicles_are_handed_over()
    {
        const string returnedVin = "WVWZZZ1JZXW000009";
        var repository = new InMemoryConvoyRepository(AConvoy(published: true))
            .WithVehicle(Vin, onConvoy: Id).WithManifest(Vin, ManifestStatus.Delivered)
            .WithVehicle(returnedVin, onConvoy: Id).WithManifest(returnedVin, ManifestStatus.Returned);
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsync($"/convoys/{Id}/arrive", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var convoy = await client.GetFromJsonAsync<JsonElement>($"/convoys/{Id}", TestContext.Current.CancellationToken);
        convoy.GetProperty("arrived").GetBoolean().Should().BeTrue();
        repository.IsHandedOver(Vin).Should().BeTrue();
        repository.IsHandedOver(returnedVin).Should().BeFalse();
        repository.ConvoyOf(returnedVin).Should().BeNull();
    }

    [Fact]
    public async Task A_convoy_with_a_vehicle_still_on_the_road_has_not_arrived()
    {
        var repository = new InMemoryConvoyRepository(AConvoy(published: true))
            .WithVehicle(Vin, onConvoy: Id).WithManifest(Vin, ManifestStatus.InTransit);
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsync($"/convoys/{Id}/arrive", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("detail").GetString().Should().Contain(Vin);
        repository.IsHandedOver(Vin).Should().BeFalse();
    }

    [Fact]
    public async Task A_convoy_whose_truck_list_was_never_published_cannot_arrive()
    {
        await using var api = FreedomApi.WithConvoys(new InMemoryConvoyRepository(AConvoy()), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsync($"/convoys/{Id}/arrive", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("Loader")]
    [InlineData("Mechanic")]
    public async Task Only_a_dispatcher_or_administrator_marks_arrival(string role)
    {
        await using var api = FreedomApi.WithConvoys(new InMemoryConvoyRepository(AConvoy(published: true)), roles: role);
        using var client = api.CreateClient();

        var response = await client.PostAsync($"/convoys/{Id}/arrive", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_handed_over_vehicle_is_never_offered_another_convoy()
    {
        var repository = new InMemoryConvoyRepository(AConvoy()).WithHandedOverVehicle(Vin);
        await using var api = FreedomApi.WithConvoys(repository, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsync($"/convoys/{Id}/vehicles/{Vin}", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("detail").GetString().Should().Contain("handed over");
    }

    [Fact]
    public async Task An_arrived_convoy_takes_no_crew_or_insurance_changes()
    {
        var repository = new InMemoryConvoyRepository(AnArrivedConvoy())
            .WithVehicle(Vin, onConvoy: Id).WithPerson(DriverId, "Olena", "Bondar");
        await using var api = FreedomApi.WithConvoys(repository, new InMemoryPersonRepository(APerson(DriverId)), roles: "Dispatcher");
        using var client = api.CreateClient();

        var crew = await client.PutAsync(
            $"/convoys/{Id}/vehicles/{Vin}/drivers/{DriverId}", content: null, TestContext.Current.CancellationToken);
        var insurance = await client.PutAsJsonAsync(
            $"/convoys/{Id}/vehicles/{Vin}/insurance", AnInsuranceBody(), TestContext.Current.CancellationToken);

        crew.StatusCode.Should().Be(HttpStatusCode.Conflict);
        insurance.StatusCode.Should().Be(HttpStatusCode.Conflict);
        repository.DriverIdsOf(Vin).Should().BeEmpty();
    }
}
