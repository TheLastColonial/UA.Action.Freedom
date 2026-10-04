using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// ADR 0012: a box can take only a registered receiver as its destination, and a bad reference is an
/// answer rather than a foreign-key exception.
/// </summary>
public class BoxReceiverEndpointTests
{
    private static readonly Guid Registered = new("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid Pending = new("aaaaaaaa-0000-4000-8000-000000000002");
    private static readonly Guid Suspended = new("aaaaaaaa-0000-4000-8000-000000000003");
    private static readonly Guid Unknown = new("aaaaaaaa-0000-4000-8000-0000000000ff");

    private static InMemoryReceiverRepository Receivers() => new(
        new ReceiverReadModel(Registered, "Kharkiv Regional Hospital", "Kharkiv oblast", ReceiverStatus.Registered),
        new ReceiverReadModel(Pending, "Lviv Clinic", "Lviv oblast", ReceiverStatus.Pending),
        new ReceiverReadModel(Suspended, "Odesa Shelter", "Odesa oblast", ReceiverStatus.Suspended));

    private static BoxReadModel ABox(Guid? receiverRef = null) => new(
        Id: 1, WeightKg: 0, WidthCm: null, DepthCm: null, HeightCm: null,
        ReceiverRef: receiverRef, LocationId: null, ValidatedByPersonId: null, ValidatedAt: null);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_box_can_be_created_for_a_registered_receiver()
    {
        var boxes = new InMemoryBoxRepository();
        await using var api = FreedomApi.WithBoxes(boxes, Receivers(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/boxes", new { receiverRef = Registered }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        boxes.Box(1)!.ReceiverRef.Should().Be(Registered);
    }

    [Theory]
    [MemberData(nameof(NotRegistered))]
    public async Task A_box_cannot_be_created_for_a_receiver_that_is_not_registered(Guid receiverRef)
    {
        var boxes = new InMemoryBoxRepository();
        await using var api = FreedomApi.WithBoxes(boxes, Receivers(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/boxes", new { receiverRef }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonOf(response)).GetProperty("type").GetString().Should().Be("receiver-not-registered");
        boxes.Count.Should().Be(0);
    }

    public static TheoryData<Guid> NotRegistered => new() { Pending, Suspended };

    [Fact]
    public async Task A_box_cannot_be_created_for_a_receiver_that_does_not_exist()
    {
        var boxes = new InMemoryBoxRepository();
        await using var api = FreedomApi.WithBoxes(boxes, Receivers(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/boxes", new { receiverRef = Unknown }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonOf(response)).GetProperty("type").GetString().Should().Be("receiver-not-found");
        boxes.Count.Should().Be(0);
    }

    [Fact]
    public async Task An_open_box_can_be_pointed_at_a_registered_receiver()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, Receivers(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync("/boxes/1", new { receiverRef = Registered }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        boxes.Box(1)!.ReceiverRef.Should().Be(Registered);
    }

    [Fact]
    public async Task An_open_box_cannot_be_pointed_at_a_pending_receiver()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, Receivers(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync("/boxes/1", new { receiverRef = Pending }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        boxes.Box(1)!.ReceiverRef.Should().BeNull();
    }

    [Fact]
    public async Task An_open_box_cannot_be_pointed_at_a_receiver_that_does_not_exist()
    {
        var boxes = new InMemoryBoxRepository(ABox());
        await using var api = FreedomApi.WithBoxes(boxes, Receivers(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync("/boxes/1", new { receiverRef = Unknown }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_box_whose_receiver_was_suspended_can_still_be_moved()
    {
        var boxes = new InMemoryBoxRepository(ABox(Suspended));
        await using var api = FreedomApi.WithBoxes(boxes, Receivers(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/boxes/1", new { receiverRef = Suspended, locationId = 5 }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        boxes.Box(1)!.LocationId.Should().Be(5);
    }
}
