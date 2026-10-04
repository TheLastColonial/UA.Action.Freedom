using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// P1: a ferry booking belongs to one vehicle on one convoy, outbound only, with a reference and
/// ticket details. It replaces the manifest's "ferry booking complete" tick.
/// </summary>
public class ConvoyFerryBookingEndpointTests
{
    private const int ConvoyId = 7;
    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    private static InMemoryConvoyRepository AConvoy(DateTime? arrivedAt = null) =>
        new InMemoryConvoyRepository(new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), null, ArrivedAt: arrivedAt))
            .WithVehicle(Vin, onConvoy: ConvoyId);

    private static string Route => $"/convoys/{ConvoyId}/vehicles/{Vin}/ferry";

    private static object ABooking() => new
    {
        @operator = "P&O Ferries",
        reference = "POF-48213",
        sailingAt = "2026-09-02T07:30:00Z",
        ticketDetails = "Freight, 2 occupants",
        costGbp = 310.00m,
    };

    [Fact]
    public async Task A_dispatcher_books_a_vehicles_outbound_ferry_and_it_reads_back()
    {
        await using var api = FreedomApi.WithConvoys(AConvoy(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var put = await client.PutAsJsonAsync(Route, ABooking(), TestContext.Current.CancellationToken);
        var booking = await client.GetFromJsonAsync<JsonElement>(Route, TestContext.Current.CancellationToken);

        put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        booking.GetProperty("operator").GetString().Should().Be("P&O Ferries");
        booking.GetProperty("reference").GetString().Should().Be("POF-48213");
        booking.GetProperty("ticketDetails").GetString().Should().Be("Freight, 2 occupants");
        booking.GetProperty("costGbp").GetDecimal().Should().Be(310.00m);
        booking.GetProperty("lastChangedByName").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Booking_again_replaces_the_booking()
    {
        await using var api = FreedomApi.WithConvoys(AConvoy(), roles: "Dispatcher");
        using var client = api.CreateClient();
        await client.PutAsJsonAsync(Route, ABooking(), TestContext.Current.CancellationToken);

        await client.PutAsJsonAsync(
            Route,
            new { @operator = "DFDS", reference = "DF-1", sailingAt = "2026-09-03T07:30:00Z" },
            TestContext.Current.CancellationToken);
        var booking = await client.GetFromJsonAsync<JsonElement>(Route, TestContext.Current.CancellationToken);

        booking.GetProperty("operator").GetString().Should().Be("DFDS");
        booking.GetProperty("costGbp").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_vehicle_with_no_booking_has_none()
    {
        await using var api = FreedomApi.WithConvoys(AConvoy(), roles: "Dispatcher");
        using var client = api.CreateClient();

        (await client.GetAsync(Route, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cancelling_the_booking_removes_it()
    {
        await using var api = FreedomApi.WithConvoys(AConvoy(), roles: "Dispatcher");
        using var client = api.CreateClient();
        await client.PutAsJsonAsync(Route, ABooking(), TestContext.Current.CancellationToken);

        (await client.DeleteAsync(Route, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync(Route, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync(Route, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_vehicle_that_is_not_on_the_convoy_or_has_withdrawn_cannot_be_booked()
    {
        var convoys = new InMemoryConvoyRepository(new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), null))
            .WithVehicle(Vin)
            .WithWithdrawnVehicle("WVWZZZ1JZXW000002", ConvoyId);
        await using var api = FreedomApi.WithConvoys(convoys, roles: "Dispatcher");
        using var client = api.CreateClient();

        (await client.PutAsJsonAsync(Route, ABooking(), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync(
                $"/convoys/{ConvoyId}/vehicles/WVWZZZ1JZXW000002/ferry", ABooking(), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_arrived_convoy_takes_no_more_bookings()
    {
        await using var api = FreedomApi.WithConvoys(AConvoy(arrivedAt: Departs.AddDays(4)), roles: "Dispatcher");
        using var client = api.CreateClient();

        (await client.PutAsJsonAsync(Route, ABooking(), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_booking_needs_an_operator_a_reference_and_a_sailing()
    {
        await using var api = FreedomApi.WithConvoys(AConvoy(), roles: "Dispatcher");
        using var client = api.CreateClient();

        (await client.PutAsJsonAsync(
                Route, new { @operator = "", reference = "", sailingAt = "2026-09-02T07:30:00Z", costGbp = -1 },
                TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("Loader")]
    [InlineData("Mechanic")]
    [InlineData("GroundOfficer")]
    public async Task Only_a_dispatcher_or_administrator_books_a_ferry(string role)
    {
        await using var api = FreedomApi.WithConvoys(AConvoy(), roles: role);
        using var client = api.CreateClient();

        (await client.PutAsJsonAsync(Route, ABooking(), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
