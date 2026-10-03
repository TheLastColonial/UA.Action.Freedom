using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// ADR 0012 from the outside: a receiver is pending until an Administrator registers it, nobody else
/// can, and what a status change touches is visible without disclosing an address.
/// </summary>
public class ReceiverRegistrationEndpointTests
{
    private static readonly Guid Ref = new("c0ffee00-0000-4000-8000-000000000001");

    private static ReceiverReadModel AReceiver(ReceiverStatus status = ReceiverStatus.Pending) =>
        new(Ref, "Kharkiv Regional Hospital", "Kharkiv oblast", status);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_receiver_registered_through_the_api_starts_pending()
    {
        await using var api = FreedomApi.WithReceivers(
            new InMemoryReceiverRepository(), new InMemoryReceiverDetailRepository(), roles: "GroundOfficer");
        using var client = api.CreateClient();

        var created = await client.PostAsJsonAsync(
            "/receivers",
            new { organisation = "Kharkiv Regional Hospital", region = "Kharkiv oblast" },
            TestContext.Current.CancellationToken);
        var receiver = await JsonOf(await client.GetAsync(created.Headers.Location, TestContext.Current.CancellationToken));

        receiver.GetProperty("status").GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task An_administrator_registers_a_receiver()
    {
        var receivers = new InMemoryReceiverRepository(AReceiver());
        await using var api = FreedomApi.WithReceivers(receivers, new InMemoryReceiverDetailRepository(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/receivers/{Ref}/status", new { status = "Registered" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        receivers.Receiver(Ref)!.Status.Should().Be(ReceiverStatus.Registered);
        var read = await JsonOf(await client.GetAsync($"/receivers/{Ref}", TestContext.Current.CancellationToken));
        read.GetProperty("status").GetString().Should().Be("Registered");
    }

    [Theory]
    [InlineData("GroundOfficer")]
    [InlineData("Dispatcher")]
    [InlineData("Loader")]
    [InlineData("Purchaser")]
    [InlineData("Mechanic")]
    public async Task Nobody_but_an_administrator_can_change_a_receivers_status(string role)
    {
        // The Ground Officer writes the receiver and still cannot grant its registration.
        var receivers = new InMemoryReceiverRepository(AReceiver());
        await using var api = FreedomApi.WithReceivers(receivers, new InMemoryReceiverDetailRepository(), roles: role);
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/receivers/{Ref}/status", new { status = "Registered" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        receivers.Receiver(Ref)!.Status.Should().Be(ReceiverStatus.Pending);
    }

    [Fact]
    public async Task Changing_a_status_without_a_token_is_unauthorized()
    {
        await using var api = FreedomApi.WithReceivers(
            new InMemoryReceiverRepository(AReceiver()), new InMemoryReceiverDetailRepository(), authenticated: false);
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/receivers/{Ref}/status", new { status = "Registered" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_ordinary_edit_cannot_register_a_receiver_whatever_the_body_says()
    {
        var receivers = new InMemoryReceiverRepository(AReceiver());
        await using var api = FreedomApi.WithReceivers(receivers, new InMemoryReceiverDetailRepository(), roles: "GroundOfficer");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/receivers/{Ref}",
            new { organisation = "Kharkiv Hospital No. 2", region = "Kharkiv oblast", status = "Registered" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        receivers.Receiver(Ref)!.Organisation.Should().Be("Kharkiv Hospital No. 2");
        receivers.Receiver(Ref)!.Status.Should().Be(ReceiverStatus.Pending);
    }

    [Fact]
    public async Task A_status_the_api_does_not_know_is_refused()
    {
        var receivers = new InMemoryReceiverRepository(AReceiver());
        await using var api = FreedomApi.WithReceivers(receivers, new InMemoryReceiverDetailRepository(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/receivers/{Ref}/status", new { status = 7 }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        receivers.Receiver(Ref)!.Status.Should().Be(ReceiverStatus.Pending);
    }

    [Fact]
    public async Task Changing_the_status_of_an_unknown_receiver_is_a_404()
    {
        await using var api = FreedomApi.WithReceivers(
            new InMemoryReceiverRepository(), new InMemoryReceiverDetailRepository(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/receivers/{Ref}/status", new { status = "Registered" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_registered_receiver_can_be_suspended_and_registered_again()
    {
        var receivers = new InMemoryReceiverRepository(AReceiver(ReceiverStatus.Registered));
        await using var api = FreedomApi.WithReceivers(receivers, new InMemoryReceiverDetailRepository(), roles: "Administrator");
        using var client = api.CreateClient();

        await client.PutAsJsonAsync($"/receivers/{Ref}/status", new { status = "Suspended" }, TestContext.Current.CancellationToken);
        receivers.Receiver(Ref)!.Status.Should().Be(ReceiverStatus.Suspended);

        await client.PutAsJsonAsync($"/receivers/{Ref}/status", new { status = "Registered" }, TestContext.Current.CancellationToken);
        receivers.Receiver(Ref)!.Status.Should().Be(ReceiverStatus.Registered);
    }

    [Fact]
    public async Task The_usage_of_a_receiver_lists_boxes_and_convoys_by_identifier_only()
    {
        var receivers = new InMemoryReceiverRepository(AReceiver(ReceiverStatus.Registered))
            .WithUsage(Ref, boxIds: [4, 9], convoyIds: [2]);
        await using var api = FreedomApi.WithReceivers(receivers, new InMemoryReceiverDetailRepository(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/receivers/{Ref}/usage", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var usage = await JsonOf(response);
        usage.GetProperty("boxIds").EnumerateArray().Select(id => id.GetInt32()).Should().Equal(4, 9);
        usage.GetProperty("convoyIds").EnumerateArray().Select(id => id.GetInt32()).Should().Equal(2);
        usage.GetProperty("boxCount").GetInt32().Should().Be(2);
        usage.GetProperty("convoyCount").GetInt32().Should().Be(1);

        // Structural, like ReceiverReadModel: nothing here can carry an address or a contact.
        usage.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("boxIds", "convoyIds", "boxCount", "convoyCount");
    }

    [Theory]
    [InlineData("GroundOfficer")]
    [InlineData("Dispatcher")]
    public async Task The_usage_of_a_receiver_is_for_the_administrator_alone(string role)
    {
        await using var api = FreedomApi.WithReceivers(
            new InMemoryReceiverRepository(AReceiver()), new InMemoryReceiverDetailRepository(), roles: role);
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/receivers/{Ref}/usage", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_usage_of_an_unknown_receiver_is_a_404()
    {
        await using var api = FreedomApi.WithReceivers(
            new InMemoryReceiverRepository(), new InMemoryReceiverDetailRepository(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/receivers/{Ref}/usage", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
