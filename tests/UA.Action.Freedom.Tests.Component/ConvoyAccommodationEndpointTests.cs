using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// P2, P8, P13, P16, O4, O30: accommodation is booked per crew member at a route point, may be shared, must cover every
/// crew member at every overnight stop (a booking, or a flag that they arrange their own), and a booking that outlives
/// its guest's place on the crew is a warning and a Dispatcher task, not a block.
/// </summary>
public class ConvoyAccommodationEndpointTests
{
    private const int ConvoyId = 7;
    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly Guid Anna = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Boris = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Carla = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    private sealed record World(
        InMemoryConvoyRepository Convoys,
        InMemoryAccommodationRepository Accommodation,
        int Lille,
        int Reims,
        int Metz);

    private static string Stays => $"/convoys/{ConvoyId}/accommodation";

    private static RouteStopReadModel AStop(int sequence, string name, RoutePointKind kind) =>
        new(sequence, null, null, name, "France", "", "FR", 0, name, kind);

    private static World AWorld(bool arrived = false)
    {
        var convoys = new InMemoryConvoyRepository(
                new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), Departs.AddDays(-5), arrived ? Departs.AddDays(4) : null))
            .WithVehicle(Vin, onConvoy: ConvoyId)
            .WithPerson(Anna, "Anna", "One")
            .WithPerson(Boris, "Boris", "Two")
            .WithPerson(Carla, "Carla", "Three");

        // A convoy that has arrived has no travelling vehicles, so nothing can be crewed on it.
        if (!arrived)
        {
            convoys.WithCrew(Vin, Anna).WithCrew(Vin, Boris);
        }

        convoys.ReplaceRouteAsync(
            ConvoyId,
            [AStop(1, "Lille", RoutePointKind.Overnight), AStop(2, "Reims", RoutePointKind.Overnight), AStop(3, "Metz", RoutePointKind.Stop)],
            CancellationToken.None).GetAwaiter().GetResult();

        var points = convoys.RouteOf(ConvoyId).Select(stop => stop.RoutePointId).ToList();
        return new World(convoys, new InMemoryAccommodationRepository(convoys), points[0], points[1], points[2]);
    }

    private static object ABooking(int routePointId, decimal? costGbp = 120m, params Guid[] guests) => new
    {
        routePointId,
        provider = "Ibis Lille",
        reference = "REF-1",
        checkIn = Departs.AddDays(1),
        checkOut = Departs.AddDays(2),
        details = "Two twin rooms",
        costGbp,
        guests,
    };

    private static async Task<int> BookAsync(HttpClient client, int routePointId, decimal? cost = 120m, params Guid[] guests)
    {
        var response = await client.PostAsJsonAsync(Stays, ABooking(routePointId, cost, guests), TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("id").GetInt32();
    }

    private static Task<JsonElement> CoverageAsync(HttpClient client) =>
        client.GetFromJsonAsync<JsonElement>($"{Stays}/coverage", TestContext.Current.CancellationToken);

    private static Task<JsonElement> TasksAsync(HttpClient client) =>
        client.GetFromJsonAsync<JsonElement>($"/convoys/{ConvoyId}/tasks", TestContext.Current.CancellationToken);

    private static string StatusOf(JsonElement coverage, int routePointId, Guid person) =>
        coverage.GetProperty("cells").EnumerateArray()
            .Single(cell => cell.GetProperty("routePointId").GetInt32() == routePointId
                && cell.GetProperty("personId").GetGuid() == person)
            .GetProperty("status").GetString()!;

    [Fact]
    public async Task A_dispatcher_books_one_shared_room_and_it_reads_back_with_its_guests_and_who_made_it()
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: "Dispatcher");
        using var client = api.CreateClient();

        var id = await BookAsync(client, world.Lille, 120m, Anna, Boris);
        var all = await client.GetFromJsonAsync<JsonElement>(Stays, TestContext.Current.CancellationToken);

        var booking = all.GetProperty("bookings").EnumerateArray().Single();
        booking.GetProperty("id").GetInt32().Should().Be(id);
        booking.GetProperty("routePointId").GetInt32().Should().Be(world.Lille);
        booking.GetProperty("provider").GetString().Should().Be("Ibis Lille");
        booking.GetProperty("costGbp").GetDecimal().Should().Be(120m);
        booking.GetProperty("cancelled").GetBoolean().Should().BeFalse();
        booking.GetProperty("guests").EnumerateArray().Select(guest => guest.GetGuid()).Should().BeEquivalentTo([Anna, Boris]);
        booking.GetProperty("lastChangedByName").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task The_grid_has_a_cell_per_overnight_stop_per_crew_member_and_shows_exactly_the_missing_one()
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: "Dispatcher");
        using var client = api.CreateClient();
        await BookAsync(client, world.Lille, 120m, Anna, Boris);
        await client.PutAsync($"{Stays}/self/{world.Reims}/{Anna}", content: null, TestContext.Current.CancellationToken);

        var coverage = await CoverageAsync(client);

        coverage.GetProperty("cells").GetArrayLength().Should().Be(4);
        StatusOf(coverage, world.Lille, Anna).Should().Be("Booked");
        StatusOf(coverage, world.Lille, Boris).Should().Be("Booked");
        StatusOf(coverage, world.Reims, Anna).Should().Be("SelfArranged");
        StatusOf(coverage, world.Reims, Boris).Should().Be("Missing");
        coverage.GetProperty("missingCount").GetInt32().Should().Be(1);
        coverage.GetProperty("allCovered").GetBoolean().Should().BeFalse();
        coverage.GetProperty("stops").EnumerateArray().Select(stop => stop.GetProperty("name").GetString())
            .Should().Equal("Lille", "Reims");
        coverage.GetProperty("crew").EnumerateArray().Select(person => person.GetProperty("name").GetString())
            .Should().BeEquivalentTo("Anna One", "Boris Two");
    }

    [Fact]
    public async Task Covering_the_last_missing_cell_makes_every_night_covered()
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: "Dispatcher");
        using var client = api.CreateClient();
        await BookAsync(client, world.Lille, 120m, Anna, Boris);
        await BookAsync(client, world.Reims, null, Anna, Boris);

        var coverage = await CoverageAsync(client);

        coverage.GetProperty("allCovered").GetBoolean().Should().BeTrue();
        coverage.GetProperty("missingCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task A_flag_is_removed_with_its_delete_and_the_cell_is_missing_again()
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: "Dispatcher");
        using var client = api.CreateClient();
        await client.PutAsync($"{Stays}/self/{world.Lille}/{Anna}", content: null, TestContext.Current.CancellationToken);

        var delete = await client.DeleteAsync($"{Stays}/self/{world.Lille}/{Anna}", TestContext.Current.CancellationToken);
        var again = await client.DeleteAsync($"{Stays}/self/{world.Lille}/{Anna}", TestContext.Current.CancellationToken);

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
        StatusOf(await CoverageAsync(client), world.Lille, Anna).Should().Be("Missing");
    }

    [Fact]
    public async Task A_stop_that_is_not_overnight_cannot_be_flagged_and_neither_can_someone_uncrewed()
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: "Dispatcher");
        using var client = api.CreateClient();

        var notOvernight = await client.PutAsync($"{Stays}/self/{world.Metz}/{Anna}", null, TestContext.Current.CancellationToken);
        var uncrewed = await client.PutAsync($"{Stays}/self/{world.Lille}/{Carla}", null, TestContext.Current.CancellationToken);
        var noSuchPoint = await client.PutAsync($"{Stays}/self/999/{Anna}", null, TestContext.Current.CancellationToken);

        notOvernight.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        uncrewed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        noSuchPoint.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_booking_names_only_crew_and_only_a_route_point_of_this_convoy()
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: "Dispatcher");
        using var client = api.CreateClient();

        var uncrewed = await client.PostAsJsonAsync(Stays, ABooking(world.Lille, 10m, Carla), TestContext.Current.CancellationToken);
        var noPoint = await client.PostAsJsonAsync(Stays, ABooking(999, 10m, Anna), TestContext.Current.CancellationToken);

        uncrewed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        noPoint.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.GetFromJsonAsync<JsonElement>(Stays, TestContext.Current.CancellationToken))
            .GetProperty("bookings").GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("""{"routePointId":1,"provider":"","checkIn":"2026-09-02T15:00:00Z","checkOut":"2026-09-03T10:00:00Z","guests":["00000000-0000-0000-0000-00000000000a"]}""")]
    [InlineData("""{"routePointId":1,"provider":"Ibis","checkIn":"2026-09-03T15:00:00Z","checkOut":"2026-09-02T10:00:00Z","guests":["00000000-0000-0000-0000-00000000000a"]}""")]
    [InlineData("""{"routePointId":1,"provider":"Ibis","checkIn":"2026-09-02T15:00:00Z","checkOut":"2026-09-03T10:00:00Z","guests":[]}""")]
    [InlineData("""{"routePointId":1,"provider":"Ibis","checkIn":"2026-09-02T15:00:00Z","checkOut":"2026-09-03T10:00:00Z","costGbp":-5,"guests":["00000000-0000-0000-0000-00000000000a"]}""")]
    public async Task A_booking_with_no_provider_backwards_dates_no_guests_or_a_negative_cost_is_refused(string body)
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsync(
            Stays, new StringContent(body, System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Replacing_a_booking_changes_its_guests_and_cancelling_it_stops_it_covering_anyone()
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: "Dispatcher");
        using var client = api.CreateClient();
        var id = await BookAsync(client, world.Lille, 120m, Anna);

        var put = await client.PutAsJsonAsync($"{Stays}/{id}", ABooking(world.Reims, 90m, Boris), TestContext.Current.CancellationToken);
        var coverage = await CoverageAsync(client);
        var cancel = await client.DeleteAsync($"{Stays}/{id}", TestContext.Current.CancellationToken);
        var cancelledAgain = await client.DeleteAsync($"{Stays}/{id}", TestContext.Current.CancellationToken);
        var after = await CoverageAsync(client);
        var replaceCancelled = await client.PutAsJsonAsync($"{Stays}/{id}", ABooking(world.Lille, 1m, Anna), TestContext.Current.CancellationToken);

        put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        StatusOf(coverage, world.Reims, Boris).Should().Be("Booked");
        StatusOf(coverage, world.Lille, Anna).Should().Be("Missing");
        cancel.StatusCode.Should().Be(HttpStatusCode.NoContent);
        cancelledAgain.StatusCode.Should().Be(HttpStatusCode.NoContent);
        StatusOf(after, world.Reims, Boris).Should().Be("Missing");
        replaceCancelled.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetFromJsonAsync<JsonElement>(Stays, TestContext.Current.CancellationToken))
            .GetProperty("bookings")[0].GetProperty("cancelled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_booking_or_convoy_is_not_found()
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: "Dispatcher");
        using var client = api.CreateClient();

        (await client.PutAsJsonAsync($"{Stays}/99", ABooking(world.Lille, 1m, Anna), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"{Stays}/99", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/convoys/999/accommodation", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/convoys/999/accommodation/coverage", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Nothing_about_the_accommodation_changes_once_the_convoy_has_arrived()
    {
        var world = AWorld(arrived: true);
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: "Dispatcher");
        using var client = api.CreateClient();

        (await client.PostAsJsonAsync(Stays, ABooking(world.Lille, 1m, Anna), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PutAsync($"{Stays}/self/{world.Lille}/{Anna}", null, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Removing_a_booked_driver_leaves_the_booking_as_a_warning_and_a_task_that_cancelling_clears()
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: "Dispatcher");
        using var client = api.CreateClient();
        var id = await BookAsync(client, world.Lille, 120m, Anna);
        (await TasksAsync(client)).GetArrayLength().Should().Be(0);

        (await client.DeleteAsync($"/convoys/{ConvoyId}/vehicles/{Vin}/crew/{Anna}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var coverage = await CoverageAsync(client);
        coverage.GetProperty("warnings").GetArrayLength().Should().Be(1);
        coverage.GetProperty("leftoverBookings")[0].GetProperty("bookingId").GetInt32().Should().Be(id);
        var task = (await TasksAsync(client)).EnumerateArray().Single();
        task.GetProperty("type").GetString().Should().Be("accommodation-leftover");
        task.GetProperty("bookingId").GetInt32().Should().Be(id);
        task.GetProperty("routePointId").GetInt32().Should().Be(world.Lille);
        task.GetProperty("resolution").GetString().Should().Be("CancelOrMigrate");

        await client.DeleteAsync($"{Stays}/{id}", TestContext.Current.CancellationToken);

        (await TasksAsync(client)).GetArrayLength().Should().Be(0);
        (await CoverageAsync(client)).GetProperty("warnings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_leftover_booking_migrates_to_the_replacement_and_the_task_clears()
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: "Dispatcher");
        using var client = api.CreateClient();
        var id = await BookAsync(client, world.Lille, 120m, Anna);
        (await client.DeleteAsync($"/convoys/{ConvoyId}/vehicles/{Vin}/crew/{Anna}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        world.Convoys.WithCrew(Vin, Carla);

        var migrate = await client.PostAsJsonAsync(
            $"{Stays}/{id}/migrate", new { fromPersonId = Anna, toPersonId = Carla }, TestContext.Current.CancellationToken);

        migrate.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await TasksAsync(client)).GetArrayLength().Should().Be(0);
        StatusOf(await CoverageAsync(client), world.Lille, Carla).Should().Be("Booked");
        (await client.GetFromJsonAsync<JsonElement>(Stays, TestContext.Current.CancellationToken))
            .GetProperty("bookings")[0].GetProperty("guests").EnumerateArray().Select(guest => guest.GetGuid())
            .Should().Equal(Carla);
    }

    [Fact]
    public async Task Migrating_needs_a_crewed_replacement_and_a_guest_to_move()
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: "Dispatcher");
        using var client = api.CreateClient();
        var id = await BookAsync(client, world.Lille, 120m, Anna);

        var uncrewed = await client.PostAsJsonAsync(
            $"{Stays}/{id}/migrate", new { fromPersonId = Anna, toPersonId = Carla }, TestContext.Current.CancellationToken);
        var notAGuest = await client.PostAsJsonAsync(
            $"{Stays}/{id}/migrate", new { fromPersonId = Boris, toPersonId = Anna }, TestContext.Current.CancellationToken);
        var same = await client.PostAsJsonAsync(
            $"{Stays}/{id}/migrate", new { fromPersonId = Anna, toPersonId = Anna }, TestContext.Current.CancellationToken);

        uncrewed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        notAGuest.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        same.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_route_point_with_a_booking_or_a_flag_cannot_be_removed_from_the_route()
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: "Dispatcher");
        using var client = api.CreateClient();
        await BookAsync(client, world.Lille, 120m, Anna);
        await client.PutAsync($"{Stays}/self/{world.Reims}/{Boris}", null, TestContext.Current.CancellationToken);

        object Stop(int id, string name) => new
        {
            routePointId = id, name, kind = "Overnight", city = name, country = "France", countryCode = "FR", postcode = "59000",
        };

        var removesBooked = await client.PutAsJsonAsync(
            $"/convoys/{ConvoyId}/route", new { stops = new[] { Stop(world.Reims, "Reims") } }, TestContext.Current.CancellationToken);
        var removesFlagged = await client.PutAsJsonAsync(
            $"/convoys/{ConvoyId}/route", new { stops = new[] { Stop(world.Lille, "Lille") } }, TestContext.Current.CancellationToken);
        var removesFree = await client.PutAsJsonAsync(
            $"/convoys/{ConvoyId}/route",
            new { stops = new[] { Stop(world.Lille, "Lille"), Stop(world.Reims, "Reims") } },
            TestContext.Current.CancellationToken);

        removesBooked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        removesFlagged.StatusCode.Should().Be(HttpStatusCode.Conflict);
        removesFree.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_hotel_line_of_the_budget_reads_the_booking_cost_and_a_cancelled_booking_drops_out()
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: "Dispatcher");
        using var client = api.CreateClient();
        var id = await BookAsync(client, world.Lille, 120m, Anna);

        decimal Hotel(JsonElement summary) => summary.GetProperty("lines").EnumerateArray()
            .Single(line => line.GetProperty("type").GetString() == "Hotel").GetProperty("actualGbp").GetDecimal();

        var before = await client.GetFromJsonAsync<JsonElement>($"/convoys/{ConvoyId}/budget/summary", TestContext.Current.CancellationToken);
        await client.DeleteAsync($"{Stays}/{id}", TestContext.Current.CancellationToken);
        var after = await client.GetFromJsonAsync<JsonElement>($"/convoys/{ConvoyId}/budget/summary", TestContext.Current.CancellationToken);

        Hotel(before).Should().Be(120m);
        Hotel(after).Should().Be(0m);
    }

    [Theory]
    [InlineData("Administrator", HttpStatusCode.OK)]
    [InlineData("Dispatcher", HttpStatusCode.OK)]
    [InlineData("Loader", HttpStatusCode.OK)]
    [InlineData("Purchaser", HttpStatusCode.OK)]
    [InlineData("Mechanic", HttpStatusCode.Forbidden)]
    [InlineData("GroundOfficer", HttpStatusCode.Forbidden)]
    public async Task Reading_the_accommodation_follows_the_convoy_read_policy(string role, HttpStatusCode expected)
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: role);
        using var client = api.CreateClient();

        (await client.GetAsync(Stays, TestContext.Current.CancellationToken)).StatusCode.Should().Be(expected);
        (await client.GetAsync($"{Stays}/coverage", TestContext.Current.CancellationToken)).StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData("Administrator", HttpStatusCode.Created)]
    [InlineData("Dispatcher", HttpStatusCode.Created)]
    [InlineData("Loader", HttpStatusCode.Forbidden)]
    [InlineData("Purchaser", HttpStatusCode.Forbidden)]
    [InlineData("Mechanic", HttpStatusCode.Forbidden)]
    [InlineData("GroundOfficer", HttpStatusCode.Forbidden)]
    public async Task Writing_the_accommodation_follows_the_convoy_write_policy(string role, HttpStatusCode expected)
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, roles: role);
        using var client = api.CreateClient();

        var book = await client.PostAsJsonAsync(Stays, ABooking(world.Lille, 1m, Anna), TestContext.Current.CancellationToken);
        var flag = await client.PutAsync($"{Stays}/self/{world.Reims}/{Anna}", null, TestContext.Current.CancellationToken);

        book.StatusCode.Should().Be(expected);
        (flag.StatusCode == HttpStatusCode.NoContent).Should().Be(expected == HttpStatusCode.Created);
    }

    [Fact]
    public async Task Without_a_login_the_accommodation_is_unauthorised()
    {
        var world = AWorld();
        await using var api = FreedomApi.WithAccommodation(world.Convoys, world.Accommodation, authenticated: false);
        using var client = api.CreateClient();

        (await client.GetAsync(Stays, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
