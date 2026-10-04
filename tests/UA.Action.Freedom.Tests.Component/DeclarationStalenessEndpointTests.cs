using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// ADR 0005 from the outside: a declaration stores the load it was written from and is stale whenever the
/// load now differs. Nothing sets the flag, so moving a box is enough to make both vehicles' declarations
/// read Stale.
/// </summary>
public class DeclarationStalenessEndpointTests
{
    private const int ConvoyId = 42;
    private const string VinA = "WVWZZZ1JZXW000001";
    private const string VinB = "WVWZZZ1JZXW000002";
    private const int BoxId = 3;
    private static readonly Guid ReceiverRef = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Published = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);

    private static string Declarations(string vin) => $"/convoys/{ConvoyId}/vehicles/{vin}/declarations";

    private static string BoxOn(string vin) => $"/convoys/{ConvoyId}/vehicles/{vin}/boxes/{BoxId}";

    private sealed record World(
        WebApplicationFactory<Program> Api,
        InMemoryReceiverRepository Receivers,
        InMemoryBoxRepository Boxes) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Api.DisposeAsync();
    }

    /// <summary>Two vehicles on one convoy and a registered Receiver; one box with one item, not yet on a vehicle.</summary>
    private static World AWorld(params string[] roles)
    {
        var convoys = new InMemoryConvoyRepository(new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), Published))
            .WithVehicle(VinA, onConvoy: ConvoyId)
            .WithVehicle(VinB, onConvoy: ConvoyId)
            .WithKnownBox(new ManifestBoxReadModel(BoxId, 12, Validated: true));
        var receivers = new InMemoryReceiverRepository(
            new ReceiverReadModel(ReceiverRef, "Hospital 4", "Lviv", ReceiverStatus.Registered));
        var boxes = new InMemoryBoxRepository(
                new BoxReadModel(BoxId, 12, null, null, null, ReceiverRef, null, null, null))
            .WithItem(
                BoxId,
                new BoxItemReadModel(Guid.NewGuid(), "Bandages", new Dictionary<string, string>(), 3, Quantity: 10));

        var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(), convoys, new InMemoryPersonRepository().CalledByALinkedVolunteer(),
            new RecordingManifestWorkQueue(), boxes: boxes, receivers: receivers, roles: roles);

        return new World(api, receivers, boxes);
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string vin) =>
        await client.GetFromJsonAsync<JsonElement>(Declarations(vin), TestContext.Current.CancellationToken);

    private static string? StatusOf(JsonElement list, string kind) =>
        list.EnumerateArray().FirstOrDefault(d => d.GetProperty("kind").GetString() == kind)
            is { ValueKind: JsonValueKind.Object } declaration
            ? declaration.GetProperty("status").GetString()
            : null;

    private static Task<HttpResponseMessage> RecordGmr(HttpClient client, string vin, string reference = "GMR-1") =>
        client.PostAsJsonAsync(
            $"{Declarations(vin)}/gmr/record", new { reference }, TestContext.Current.CancellationToken);

    // ---- increment 2: marking a declaration ready --------------------------------------------------

    [Fact]
    public async Task A_dispatcher_marks_a_declaration_ready_and_it_reads_ready_to_file()
    {
        await using var world = AWorld("Dispatcher");
        using var client = world.Api.CreateClient();

        var response = await client.PostAsync(
            $"{Declarations(VinA)}/gmr/ready", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        StatusOf(await ListAsync(client, VinA), "Gmr").Should().Be("ReadyToFile");
    }

    [Fact]
    public async Task The_stored_snapshot_is_never_part_of_the_json()
    {
        await using var world = AWorld("Dispatcher");
        using var client = world.Api.CreateClient();
        await client.PostAsync($"{Declarations(VinA)}/gmr/ready", content: null, TestContext.Current.CancellationToken);

        var declaration = (await ListAsync(client, VinA)).EnumerateArray().Single();

        declaration.EnumerateObject().Select(property => property.Name.ToLowerInvariant())
            .Should().NotContain(["snapshotjson", "snapshotversion", "snapshot"]);
    }

    [Fact]
    public async Task A_declaration_that_has_been_filed_cannot_be_marked_ready_again()
    {
        await using var world = AWorld("Dispatcher");
        using var client = world.Api.CreateClient();
        await RecordGmr(client, VinA);

        var response = await client.PostAsync(
            $"{Declarations(VinA)}/gmr/ready", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_goods_list_is_marked_ready_for_the_receiver_it_is_for()
    {
        await using var world = AWorld("Dispatcher");
        using var client = world.Api.CreateClient();

        var without = await client.PostAsync(
            $"{Declarations(VinA)}/goods-list/ready", content: null, TestContext.Current.CancellationToken);
        var with = await client.PostAsync(
            $"{Declarations(VinA)}/goods-list/ready?receiverRef={ReceiverRef}", content: null,
            TestContext.Current.CancellationToken);

        without.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        with.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Marking_a_declaration_ready_on_a_vehicle_not_on_the_convoy_is_not_found()
    {
        await using var world = AWorld("Dispatcher");
        using var client = world.Api.CreateClient();

        var response = await client.PostAsync(
            $"/convoys/{ConvoyId}/vehicles/NOSUCHVIN000000/declarations/gmr/ready", content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("GroundOfficer", HttpStatusCode.Forbidden)]
    [InlineData("Loader", HttpStatusCode.Forbidden)]
    public async Task Only_those_who_declare_may_mark_a_declaration_ready(string role, HttpStatusCode expected)
    {
        await using var world = AWorld(role);
        using var client = world.Api.CreateClient();

        var response = await client.PostAsync(
            $"{Declarations(VinA)}/gmr/ready", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(expected);
    }

    // ---- increment 3: staleness is visible --------------------------------------------------------

    [Fact]
    public async Task Moving_a_box_to_another_vehicle_makes_both_vehicles_declarations_stale()
    {
        await using var world = AWorld("Administrator");
        using var client = world.Api.CreateClient();
        await client.PutAsync(BoxOn(VinA), content: null, TestContext.Current.CancellationToken);
        await client.PostAsync($"{Declarations(VinA)}/gmr/ready", content: null, TestContext.Current.CancellationToken);
        await client.PostAsync($"{Declarations(VinB)}/gmr/ready", content: null, TestContext.Current.CancellationToken);
        await RecordGmr(client, VinA, "GMR-A");
        await RecordGmr(client, VinB, "GMR-B");
        StatusOf(await ListAsync(client, VinA), "Gmr").Should().Be("Filed");
        StatusOf(await ListAsync(client, VinB), "Gmr").Should().Be("Filed");

        await client.PutAsync(BoxOn(VinB), content: null, TestContext.Current.CancellationToken);

        StatusOf(await ListAsync(client, VinA), "Gmr").Should().Be("Stale");
        StatusOf(await ListAsync(client, VinB), "Gmr").Should().Be("Stale");
    }

    [Fact]
    public async Task A_declaration_recorded_without_being_marked_ready_still_goes_stale()
    {
        await using var world = AWorld("Administrator");
        using var client = world.Api.CreateClient();
        await client.PutAsync(BoxOn(VinA), content: null, TestContext.Current.CancellationToken);
        await RecordGmr(client, VinA);

        await client.DeleteAsync(BoxOn(VinA), TestContext.Current.CancellationToken);

        StatusOf(await ListAsync(client, VinA), "Gmr").Should().Be("Stale");
    }

    [Fact]
    public async Task A_load_that_has_not_changed_leaves_the_declaration_as_it_was()
    {
        await using var world = AWorld("Administrator");
        using var client = world.Api.CreateClient();
        await client.PutAsync(BoxOn(VinA), content: null, TestContext.Current.CancellationToken);
        await RecordGmr(client, VinA);

        StatusOf(await ListAsync(client, VinA), "Gmr").Should().Be("Filed");
    }

    [Fact]
    public async Task Changing_the_items_in_a_filed_load_makes_the_declaration_stale()
    {
        await using var world = AWorld("Administrator");
        using var client = world.Api.CreateClient();
        await client.PutAsync(BoxOn(VinA), content: null, TestContext.Current.CancellationToken);
        await RecordGmr(client, VinA);

        await world.Boxes.AddItemAsync(
            BoxId,
            new BoxItemReadModel(Guid.NewGuid(), "Blankets", new Dictionary<string, string>(), 4, Quantity: 2),
            TestContext.Current.CancellationToken);

        StatusOf(await ListAsync(client, VinA), "Gmr").Should().Be("Stale");
    }

    // ---- increment 4: a Receiver losing registration ----------------------------------------------

    [Fact]
    public async Task A_receiver_that_stops_being_registered_makes_the_declarations_naming_it_stale()
    {
        await using var world = AWorld("Administrator");
        using var client = world.Api.CreateClient();
        await client.PutAsync(BoxOn(VinA), content: null, TestContext.Current.CancellationToken);
        await RecordGmr(client, VinA);

        await world.Receivers.SetStatusAsync(ReceiverRef, ReceiverStatus.Suspended, TestContext.Current.CancellationToken);

        StatusOf(await ListAsync(client, VinA), "Gmr").Should().Be("Stale");
    }

    // ---- increment 5: the re-declare task, and withdrawing ----------------------------------------

    private static async Task<JsonElement> TasksAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>($"/convoys/{ConvoyId}/tasks", TestContext.Current.CancellationToken);

    private static async Task MoveTheBoxFromAToB(HttpClient client)
    {
        await client.PutAsync(BoxOn(VinA), content: null, TestContext.Current.CancellationToken);
        await RecordGmr(client, VinA, "GMR-A");
        await RecordGmr(client, VinB, "GMR-B");
        await client.PutAsync(BoxOn(VinB), content: null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Moving_a_box_raises_a_re_declare_task_for_each_vehicle()
    {
        await using var world = AWorld("Administrator");
        using var client = world.Api.CreateClient();

        await MoveTheBoxFromAToB(client);

        var tasks = await TasksAsync(client);
        tasks.EnumerateArray().Select(task => task.GetProperty("vin").GetString())
            .Should().BeEquivalentTo(VinA, VinB);
        tasks[0].GetProperty("kind").GetString().Should().Be("Gmr");
        tasks[0].GetProperty("resolution").GetString().Should().Be("UpdateOrRecreate");
    }

    [Fact]
    public async Task A_convoy_with_nothing_stale_has_no_tasks()
    {
        await using var world = AWorld("Administrator");
        using var client = world.Api.CreateClient();
        await client.PutAsync(BoxOn(VinA), content: null, TestContext.Current.CancellationToken);
        await RecordGmr(client, VinA);

        (await TasksAsync(client)).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Tasks_for_a_convoy_that_does_not_exist_are_not_found()
    {
        await using var world = AWorld("Administrator");
        using var client = world.Api.CreateClient();

        var response = await client.GetAsync("/convoys/999/tasks", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_ground_officer_cannot_read_the_tasks()
    {
        await using var world = AWorld("GroundOfficer");
        using var client = world.Api.CreateClient();

        var response = await client.GetAsync($"/convoys/{ConvoyId}/tasks", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Withdrawing_a_stale_declaration_clears_its_task_and_starts_a_new_draft()
    {
        await using var world = AWorld("Administrator");
        using var client = world.Api.CreateClient();
        await MoveTheBoxFromAToB(client);
        var stale = (await ListAsync(client, VinA)).EnumerateArray().Single();

        var response = await client.PostAsync(
            $"{Declarations(VinA)}/{stale.GetProperty("id").GetInt32()}/withdraw", content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await TasksAsync(client)).EnumerateArray().Select(task => task.GetProperty("vin").GetString())
            .Should().BeEquivalentTo(VinB);
        var history = (await ListAsync(client, VinA)).EnumerateArray().ToList();
        history.Select(d => d.GetProperty("status").GetString()).Should().Equal("Withdrawn", "Draft");
        history[0].GetProperty("reference").GetString().Should().Be("GMR-A");
    }

    [Fact]
    public async Task Recording_a_new_reference_after_withdrawing_leaves_the_declaration_current()
    {
        await using var world = AWorld("Administrator");
        using var client = world.Api.CreateClient();
        await MoveTheBoxFromAToB(client);
        var stale = (await ListAsync(client, VinA)).EnumerateArray().Single();
        await client.PostAsync(
            $"{Declarations(VinA)}/{stale.GetProperty("id").GetInt32()}/withdraw", content: null,
            TestContext.Current.CancellationToken);

        var record = await RecordGmr(client, VinA, "GMR-A2");

        record.StatusCode.Should().Be(HttpStatusCode.NoContent);
        StatusOf(await ListAsync(client, VinA), "Gmr").Should().NotBe("Stale");
        (await TasksAsync(client)).EnumerateArray().Select(task => task.GetProperty("vin").GetString())
            .Should().BeEquivalentTo(VinB);
    }

    [Fact]
    public async Task A_declaration_that_is_not_stale_cannot_be_withdrawn()
    {
        await using var world = AWorld("Administrator");
        using var client = world.Api.CreateClient();
        await client.PutAsync(BoxOn(VinA), content: null, TestContext.Current.CancellationToken);
        await RecordGmr(client, VinA);
        var declaration = (await ListAsync(client, VinA)).EnumerateArray().Single();

        var response = await client.PostAsync(
            $"{Declarations(VinA)}/{declaration.GetProperty("id").GetInt32()}/withdraw", content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Withdrawing_a_declaration_that_is_not_on_the_vehicle_is_not_found()
    {
        await using var world = AWorld("Administrator");
        using var client = world.Api.CreateClient();

        var response = await client.PostAsync(
            $"{Declarations(VinA)}/999/withdraw", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Withdrawing_a_stale_ens_keeps_the_old_mrn_and_a_new_one_can_be_recorded()
    {
        await using var world = AWorld("Administrator");
        using var client = world.Api.CreateClient();
        await client.PutAsync(BoxOn(VinA), content: null, TestContext.Current.CancellationToken);
        await client.PutAsJsonAsync(
            $"{Declarations(VinA)}/ens",
            new { mrn = "25FR17551780961AT5", acceptedAt = "2026-08-24T09:30:00+00:00", filedBy = "groundofficer" },
            TestContext.Current.CancellationToken);
        await client.DeleteAsync(BoxOn(VinA), TestContext.Current.CancellationToken);
        var ens = (await ListAsync(client, VinA)).EnumerateArray().Single();
        ens.GetProperty("status").GetString().Should().Be("Stale");

        await client.PostAsync(
            $"{Declarations(VinA)}/{ens.GetProperty("id").GetInt32()}/withdraw", content: null,
            TestContext.Current.CancellationToken);
        var refile = await client.PutAsJsonAsync(
            $"{Declarations(VinA)}/ens",
            new { mrn = "25FR17551780961AT6", acceptedAt = "2026-08-26T09:30:00+00:00", filedBy = "groundofficer" },
            TestContext.Current.CancellationToken);

        refile.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ListAsync(client, VinA)).EnumerateArray().Select(d => d.GetProperty("reference").GetString())
            .Should().Equal("25FR17551780961AT5", "25FR17551780961AT6");
    }
}
