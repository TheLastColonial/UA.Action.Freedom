using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// ADR 0012 and P5: a vehicle on a convoy names the Receiver it is handed over to, and that Receiver has
/// to be registered.
/// </summary>
public class ConvoyHandoverReceiverEndpointTests
{
    private const int ConvoyId = 7;
    private const string Vin = "WVWZZZ1JZXW000001";
    private static readonly Guid Registered = new("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid Pending = new("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid Unknown = new("bbbbbbbb-0000-4000-8000-0000000000ff");

    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    private static InMemoryReceiverRepository Receivers() => new(
        new ReceiverReadModel(Registered, "Kharkiv Regional Hospital", "Kharkiv oblast", ReceiverStatus.Registered),
        new ReceiverReadModel(Pending, "Lviv Clinic", "Lviv oblast", ReceiverStatus.Pending));

    private static InMemoryConvoyRepository AConvoyWithAVehicle(DateTime? arrivedAt = null) =>
        new InMemoryConvoyRepository(
                new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), null, ArrivedAt: arrivedAt))
            .WithVehicle(Vin, onConvoy: ConvoyId);

    private static string Route => $"/convoys/{ConvoyId}/vehicles/{Vin}/handover-receiver";

    [Fact]
    public async Task A_dispatcher_names_a_registered_receiver_as_the_handover_receiver()
    {
        var convoys = AConvoyWithAVehicle();
        await using var api = FreedomApi.WithConvoys(convoys, Receivers(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(Route, new { receiverRef = Registered }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        convoys.HandoverReceiverOf(ConvoyId, Vin).Should().Be(Registered);
    }

    [Fact]
    public async Task The_truck_list_shows_the_handover_receiver()
    {
        var convoys = AConvoyWithAVehicle();
        await using var api = FreedomApi.WithConvoys(convoys, Receivers(), roles: "Dispatcher");
        using var client = api.CreateClient();
        await client.PutAsJsonAsync(Route, new { receiverRef = Registered }, TestContext.Current.CancellationToken);

        var list = await client.GetFromJsonAsync<JsonElement>($"/convoys/{ConvoyId}/vehicles", TestContext.Current.CancellationToken);

        list[0].GetProperty("handoverReceiverRef").GetGuid().Should().Be(Registered);
    }

    [Fact]
    public async Task A_vehicle_with_no_handover_receiver_yet_shows_none()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithAVehicle(), Receivers(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var list = await client.GetFromJsonAsync<JsonElement>($"/convoys/{ConvoyId}/vehicles", TestContext.Current.CancellationToken);

        list[0].GetProperty("handoverReceiverRef").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_receiver_that_is_not_registered_cannot_be_the_handover_receiver()
    {
        var convoys = AConvoyWithAVehicle();
        await using var api = FreedomApi.WithConvoys(convoys, Receivers(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(Route, new { receiverRef = Pending }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        convoys.HandoverReceiverOf(ConvoyId, Vin).Should().BeNull();
    }

    [Fact]
    public async Task A_receiver_that_does_not_exist_cannot_be_the_handover_receiver()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithAVehicle(), Receivers(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(Route, new { receiverRef = Unknown }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_empty_receiver_reference_is_a_validation_problem()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithAVehicle(), Receivers(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(Route, new { receiverRef = Guid.Empty }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_vehicle_that_is_not_on_the_convoy_is_a_404()
    {
        var convoys = new InMemoryConvoyRepository(new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), null))
            .WithVehicle(Vin);
        await using var api = FreedomApi.WithConvoys(convoys, Receivers(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(Route, new { receiverRef = Registered }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unknown_convoy_is_a_404()
    {
        await using var api = FreedomApi.WithConvoys(new InMemoryConvoyRepository(), Receivers(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(Route, new { receiverRef = Registered }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_handover_receiver_of_an_arrived_convoy_cannot_change()
    {
        await using var api = FreedomApi.WithConvoys(AConvoyWithAVehicle(arrivedAt: Departs.AddDays(4)), Receivers(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(Route, new { receiverRef = Registered }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_withdrawn_vehicle_has_no_handover_receiver_to_set()
    {
        var convoys = new InMemoryConvoyRepository(new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), Departs))
            .WithWithdrawnVehicle(Vin, ConvoyId);
        await using var api = FreedomApi.WithConvoys(convoys, Receivers(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(Route, new { receiverRef = Registered }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("Loader")]
    [InlineData("Purchaser")]
    [InlineData("GroundOfficer")]
    public async Task Only_those_who_may_write_a_convoy_can_set_the_handover_receiver(string role)
    {
        var convoys = AConvoyWithAVehicle();
        await using var api = FreedomApi.WithConvoys(convoys, Receivers(), roles: role);
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(Route, new { receiverRef = Registered }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        convoys.HandoverReceiverOf(ConvoyId, Vin).Should().BeNull();
    }
}
