using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// The <c>/manifests</c> contract from the outside: the lifecycle, and the two rules the state
/// diagram cannot express.
/// </summary>
/// <remarks>
/// A manifest may only be proposed against a convoy whose truck list is published
/// (docs/process.puml), and once its Goods Movement Reference exists nothing about it may change
/// (recommendations §5.2) — the vehicle would otherwise arrive at a border carrying something
/// HMRC was not told about. Approval is where the freeze happens and where the submission is
/// handed off, and it is Administrator only.
/// </remarks>
public class ManifestEndpointTests
{
    private const string Id = "MAN-0001";
    private const int ConvoyId = 42;

    private static readonly Guid Primary = new("2b9c1e40-7d8a-4c31-9f52-6a0b8d3e5c11");
    private static readonly Guid Secondary = new("7c1d2e50-8e9b-4d42-a063-7b1c9e4f6d22");
    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    private static ManifestReadModel AManifest(
        ManifestStatus status = ManifestStatus.Created, bool frozen = false, int? convoyId = ConvoyId) => new(
        Id, "WVWZZZ1JZXW000001", convoyId, status, null, FerryBookingComplete: false,
        GmrSubmittedAt: frozen ? new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc) : null);

    private static IConvoyRepository AConvoy(bool truckListPublished = true)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyId, Arg.Any<CancellationToken>()).Returns(
            new ConvoyReadModel(
                ConvoyId, Departs, Departs.AddDays(4),
                truckListPublished ? new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc) : null));
        return convoys;
    }

    private static PersonReadModel APerson(Guid id, bool isDriver = true) => new(
        id, "Sam", "Whitfield",
        new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        null, isDriver, Committed: true);

    private static InMemoryPersonRepository ARosterOfDrivers() =>
        new(APerson(Primary), APerson(Secondary));

    [Fact]
    public async Task Reading_manifests_without_a_token_is_unauthorized()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), authenticated: false);
        using var client = api.CreateClient();

        var response = await client.GetAsync("/manifests", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_ground_officer_is_refused_manifests()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), roles: "GroundOfficer");
        using var client = api.CreateClient();

        var response = await client.GetAsync("/manifests", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_dispatcher_opens_a_manifest_in_the_created_state()
    {
        var manifests = new InMemoryManifestRepository();
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/manifests",
            new { id = Id, vin = "WVWZZZ1JZXW000001", convoyId = ConvoyId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().EndWith($"/manifests/{Id}");
        manifests.Manifest(Id)!.Status.Should().Be(ManifestStatus.Created);
    }

    [Fact]
    public async Task Reusing_a_manifest_reference_is_a_conflict()
    {
        var manifests = new InMemoryManifestRepository(AManifest());
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/manifests", new { id = Id }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_manifest_moves_along_the_happy_path()
    {
        var manifests = new InMemoryManifestRepository(AManifest());
        var queue = new RecordingManifestWorkQueue();
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), queue, roles: "Administrator");
        using var client = api.CreateClient();

        foreach (var (step, expected) in new (string Step, ManifestStatus Expected)[]
                 {
                     ("propose", ManifestStatus.Proposed),
                     ("approve", ManifestStatus.Confirmed),
                     ("prepare", ManifestStatus.Preparing),
                     ("ready", ManifestStatus.Ready),
                     ("depart", ManifestStatus.InTransit),
                     ("deliver", ManifestStatus.Delivered),
                 })
        {
            var response = await client.PostAsync(
                $"/manifests/{Id}/{step}", content: null, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.NoContent, "step '{0}' should be allowed", step);
            manifests.Manifest(Id)!.Status.Should().Be(expected);
        }
    }

    [Fact]
    public async Task Refuses_to_propose_against_a_convoy_whose_truck_list_is_still_open()
    {
        var manifests = new InMemoryManifestRepository(AManifest());
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(truckListPublished: false), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsync(
            $"/manifests/{Id}/propose", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        manifests.Manifest(Id)!.Status.Should().Be(ManifestStatus.Created);
    }

    [Fact]
    public async Task Refuses_an_edge_the_diagram_does_not_draw()
    {
        var manifests = new InMemoryManifestRepository(AManifest(ManifestStatus.Confirmed));
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsync(
            $"/manifests/{Id}/depart", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_dispatcher_may_build_a_manifest_but_not_approve_it()
    {
        // Approval releases the GMR and freezes the manifest, so the person who builds one is
        // not the person who signs it off.
        var manifests = new InMemoryManifestRepository(AManifest(ManifestStatus.Proposed));
        var queue = new RecordingManifestWorkQueue();
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), queue, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsync(
            $"/manifests/{Id}/approve", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        queue.Submissions.Should().BeEmpty();
        manifests.Manifest(Id)!.Frozen.Should().BeFalse();
    }

    [Fact]
    public async Task Approving_freezes_the_manifest_and_queues_exactly_one_submission()
    {
        var manifests = new InMemoryManifestRepository(AManifest(ManifestStatus.Proposed));
        var queue = new RecordingManifestWorkQueue();
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), queue, roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsync(
            $"/manifests/{Id}/approve", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        manifests.Manifest(Id)!.Frozen.Should().BeTrue();

        var submission = queue.Submissions.Should().ContainSingle().Subject;
        submission.ManifestId.Should().Be(Id);
        submission.VehicleRegistration.Should().Be("WVWZZZ1JZXW000001");
        submission.DepartsAt.Should().Be(Departs);
    }

    [Fact]
    public async Task Approving_also_queues_the_document_that_travels_with_the_vehicle()
    {
        // The other half of the fork in docs/process.puml. Composed here, where the database is,
        // so the worker that renders it needs no database access at all.
        var manifests = new InMemoryManifestRepository(AManifest(ManifestStatus.Proposed))
            .WithVehicleWeight(1_400)
            .WithBoxOn(Id, new ManifestBoxReadModel(1, 30, Validated: true));
        var queue = new RecordingManifestWorkQueue();
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), queue, roles: "Administrator");
        using var client = api.CreateClient();

        await client.PostAsync($"/manifests/{Id}/approve", content: null, TestContext.Current.CancellationToken);

        var document = queue.Documents.Should().ContainSingle().Subject;
        document.ManifestId.Should().Be(Id);
        document.VehicleWeightKg.Should().Be(1_400);
        document.CargoKg.Should().Be(30);
        document.TotalKg.Should().Be(1_675);
        document.Lines.Should().ContainSingle().Which.ReceiverRegion.Should().Be("Kharkiv oblast");
    }

    [Fact]
    public void The_queued_document_has_nowhere_to_put_a_delivery_address()
    {
        // Region-level is as precise as anything that travels gets. A later change cannot leak
        // an address onto the printed manifest without first adding a field to carry one.
        var lineFields = typeof(ManifestDocumentLineReadModel).GetProperties().Select(property => property.Name);

        lineFields.Should().BeEquivalentTo(
            "BoxId", "WeightKg", "ItemCount", "ReceiverOrganisation", "ReceiverRegion");
    }

    [Fact]
    public async Task The_queued_submission_carries_no_receiver_detail()
    {
        // A queue message is durable and readable by anything holding the storage credential,
        // so it is the wrong place for a Ukrainian delivery address (§4.4). The request type has
        // nowhere to put one — this asserts that stays true.
        var properties = typeof(GmrSubmissionRequest).GetProperties().Select(property => property.Name);

        properties.Should().BeEquivalentTo("ManifestId", "VehicleRegistration", "DepartsAt");
    }

    [Fact]
    public async Task A_frozen_manifest_cannot_be_put_back_in_front_of_an_approver()
    {
        // A manifest whose GMR exists must not reappear as something still editable. Progress
        // is fine — it is reopening that §5.2 forbids.
        var manifests = new InMemoryManifestRepository(AManifest(ManifestStatus.Rejected, frozen: true));
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsync(
            $"/manifests/{Id}/propose", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        manifests.Manifest(Id)!.Status.Should().Be(ManifestStatus.Rejected);
    }

    [Theory]
    [InlineData("deliver")]
    [InlineData("lose")]
    public async Task A_frozen_manifest_can_still_record_what_happened_to_the_load(string step)
    {
        var manifests = new InMemoryManifestRepository(AManifest(ManifestStatus.InTransit, frozen: true));
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsync(
            $"/manifests/{Id}/{step}", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_frozen_manifest_cannot_be_edited_recrewed_reloaded_or_deleted()
    {
        var manifests = new InMemoryManifestRepository(AManifest(ManifestStatus.Confirmed, frozen: true))
            .WithKnownBox(7);
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Administrator");
        using var client = api.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        (await client.PutAsJsonAsync($"/manifests/{Id}", new { deliveryNotes = "changed" }, cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await client.PutAsJsonAsync(
                $"/manifests/{Id}/teams/Uk",
                new { primaryPersonId = Primary, secondaryPersonId = Secondary },
                cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await client.PutAsync($"/manifests/{Id}/boxes/7", content: null, cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await client.DeleteAsync($"/manifests/{Id}", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        manifests.Count.Should().Be(1);
    }

    [Fact]
    public async Task A_dispatcher_crews_both_legs()
    {
        var manifests = new InMemoryManifestRepository(AManifest());
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        foreach (var leg in new[] { "Uk", "Border" })
        {
            var response = await client.PutAsJsonAsync(
                $"/manifests/{Id}/teams/{leg}",
                new { primaryPersonId = Primary, secondaryPersonId = Secondary },
                TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var teams = await client.GetFromJsonAsync<JsonElement>(
            $"/manifests/{Id}/teams", TestContext.Current.CancellationToken);

        teams.EnumerateArray().Should().HaveCount(2);
    }

    [Fact]
    public async Task Crewing_a_leg_twice_replaces_the_team_rather_than_adding_one()
    {
        var manifests = new InMemoryManifestRepository(AManifest());
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        await client.PutAsJsonAsync(
            $"/manifests/{Id}/teams/Uk",
            new { primaryPersonId = Primary, secondaryPersonId = Secondary },
            TestContext.Current.CancellationToken);
        await client.PutAsJsonAsync(
            $"/manifests/{Id}/teams/Uk",
            new { primaryPersonId = Secondary, secondaryPersonId = (Guid?)null },
            TestContext.Current.CancellationToken);

        var team = manifests.Teams(Id).Should().ContainSingle().Subject;
        team.PrimaryPersonId.Should().Be(Secondary);
        team.SecondaryPersonId.Should().BeNull();
    }

    [Fact]
    public async Task A_volunteer_who_does_not_drive_cannot_crew_a_leg()
    {
        var manifests = new InMemoryManifestRepository(AManifest());
        var people = new InMemoryPersonRepository(APerson(Primary, isDriver: false));
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), people, new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/manifests/{Id}/teams/Uk",
            new { primaryPersonId = Primary },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        manifests.Teams(Id).Should().BeEmpty();
    }

    [Fact]
    public async Task The_same_volunteer_cannot_crew_both_halves_of_a_pair()
    {
        var manifests = new InMemoryManifestRepository(AManifest());
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/manifests/{Id}/teams/Uk",
            new { primaryPersonId = Primary, secondaryPersonId = Primary },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_border_weight_shows_its_fixed_allowances_and_flags_unweighed_cargo()
    {
        var manifests = new InMemoryManifestRepository(AManifest())
            .WithVehicleWeight(1_400)
            .WithBoxOn(Id, new ManifestBoxReadModel(1, 30, Validated: true))
            .WithBoxOn(Id, new ManifestBoxReadModel(2, 0, Validated: false));
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Loader");
        using var client = api.CreateClient();

        var weight = await client.GetFromJsonAsync<JsonElement>(
            $"/manifests/{Id}/weight", TestContext.Current.CancellationToken);

        weight.GetProperty("vehicleKg").GetInt32().Should().Be(1_400);
        weight.GetProperty("cargoKg").GetInt32().Should().Be(30);
        weight.GetProperty("crewAndBagsKg").GetInt32().Should().Be(200);
        weight.GetProperty("fuelKg").GetInt32().Should().Be(45);
        weight.GetProperty("totalKg").GetInt32().Should().Be(1_675);

        // The honesty flag: one box on this manifest has not been weighed by a Loader.
        weight.GetProperty("unvalidatedBoxCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Putting_an_unknown_box_on_a_manifest_is_a_404()
    {
        var manifests = new InMemoryManifestRepository(AManifest());
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            $"/manifests/{Id}/boxes/999", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Fetching_an_unknown_manifest_is_a_404()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync("/manifests/NOSUCH", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
