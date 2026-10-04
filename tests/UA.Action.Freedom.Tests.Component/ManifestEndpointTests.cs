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
/// HMRC was not told about. Approval is where the freeze happens and it is Administrator only; it
/// files nothing, because declarations are filed afterwards from the vehicle (ADR 0006).
/// </remarks>
public class ManifestEndpointTests
{
    private const string Id = "MAN-0001";
    private const int ConvoyId = 42;
    private const string Vin = "WVWZZZ1JZXW000001";

    /// <summary>Fixed, regex-valid samples of the two references French customs mints.</summary>
    private const string Jeton = "EI202512091201178668Z";
    private const string NumeroDossier = "B2025120912003386654";

    private static readonly Guid Primary = new("2b9c1e40-7d8a-4c31-9f52-6a0b8d3e5c11");
    private static readonly Guid Secondary = new("7c1d2e50-8e9b-4d42-a063-7b1c9e4f6d22");
    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Published = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Submitted = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    private static readonly byte[] Barcode =
        System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 barcode for one lorry");

    private static ManifestReadModel AManifest(
        ManifestStatus status = ManifestStatus.Created, bool frozen = false) => new(
        Id, ConvoyId, Vin, status, null,
        GmrSubmittedAt: frozen ? new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc) : null);

    /// <summary>
    /// A convoy whose truck list is published, carrying the manifest's vehicle, insured and in
    /// cover today — what departure needs — unless a test says otherwise.
    /// </summary>
    private static InMemoryConvoyRepository AConvoy(bool truckListPublished = true, bool insured = true)
    {
        var convoys = new InMemoryConvoyRepository(
                new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), truckListPublished ? Published : null))
            .WithVehicle(Vin, onConvoy: ConvoyId);

        return insured
            ? convoys.WithInsurance(new VehicleInsuranceReadModel(
                ConvoyId, Vin, "Ukraine Aid Mutual", "POL-1",
                DateTime.UtcNow.Date.AddDays(-1), DateTime.UtcNow.Date.AddDays(30), null, InMemoryPersonRepository.TestUserId,
                DateTime.UtcNow, VoidedAt: null))
            : convoys;
    }

    private static PersonReadModel APerson(Guid id, bool isDriver = true) => new(
        id, "Sam", "Whitfield",
        new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        null, isDriver, Committed: true);

    private static InMemoryPersonRepository ARosterOfDrivers() =>
        new InMemoryPersonRepository(APerson(Primary), APerson(Secondary)).CalledByALinkedVolunteer();

    /// <summary>The ICS2 MRN the crossing was accepted under.</summary>
    private const string Mrn = "25FR17551780961AT5";

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
    public async Task There_is_no_way_to_open_a_manifest_that_is_not_on_a_truck_list()
    {
        // POST /manifests is gone. A manifest is the paperwork for one vehicle on one convoy, so
        // it is opened against that truck-list entry — which is what makes (ConvoyId, Vin) a
        // foreign key rather than two fields a caller can set to anything.
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/manifests", new { id = Id, vin = Vin, convoyId = ConvoyId }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task A_manifest_moves_along_the_happy_path()
    {
        var manifests = new InMemoryManifestRepository(AManifest());
        var queue = new RecordingManifestWorkQueue();
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), queue,
            roles: "Administrator");
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
    public async Task A_vehicle_without_insurance_in_cover_does_not_depart()
    {
        var manifests = new InMemoryManifestRepository(AManifest() with { Status = ManifestStatus.Ready });
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(insured: false), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PostAsync($"/manifests/{Id}/depart", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("detail").GetString().Should().Contain("insurance");
        manifests.Manifest(Id)!.Status.Should().Be(ManifestStatus.Ready);
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
    public async Task Approving_freezes_the_manifest_and_queues_nothing()
    {
        // Approval signs off the load and nothing else (ADR 0004, ADR 0006): the GMR, the envelope and
        // the document are separate, explicit acts afterwards.
        var manifests = new InMemoryManifestRepository(AManifest(ManifestStatus.Proposed));
        var queue = new RecordingManifestWorkQueue();
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), queue,
            roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsync(
            $"/manifests/{Id}/approve", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        manifests.Manifest(Id)!.Frozen.Should().BeTrue();
        queue.Submissions.Should().BeEmpty();
        queue.Envelopes.Should().BeEmpty();
        queue.Documents.Should().BeEmpty();
    }

    [Fact]
    public async Task The_document_that_travels_with_the_vehicle_is_requested_after_approval()
    {
        // Composed here, where the database is, so the worker that renders it needs no database access.
        var manifests = new InMemoryManifestRepository(AManifest(ManifestStatus.Proposed))
            .WithVehicleWeight(1_400)
            .WithBoxOn(Id, new ManifestBoxReadModel(1, 30, Validated: true));
        var queue = new RecordingManifestWorkQueue();
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), queue,
            roles: ["Administrator", "Dispatcher"]);
        using var client = api.CreateClient();

        var before = await client.PostAsync($"/manifests/{Id}/document", null, TestContext.Current.CancellationToken);
        await client.PostAsync($"/manifests/{Id}/approve", content: null, TestContext.Current.CancellationToken);
        var after = await client.PostAsync($"/manifests/{Id}/document", null, TestContext.Current.CancellationToken);

        before.StatusCode.Should().Be(HttpStatusCode.Conflict);
        after.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var document = queue.Documents.Should().ContainSingle().Subject;
        document.ManifestId.Should().Be(Id);
        document.VehicleWeightKg.Should().Be(1_400);
        document.CargoKg.Should().Be(30);
        document.TotalKg.Should().Be(1_675);
        document.Lines.Should().ContainSingle().Which.ReceiverRegion.Should().Be("Kharkiv oblast");
    }

    [Fact]
    public async Task The_old_ens_routes_say_the_declaration_has_moved()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        (await client.GetAsync($"/manifests/{Id}/ens", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Gone);
        (await client.GetAsync($"/manifests/{Id}/ens/filing-sheet", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Gone);
        (await client.PutAsJsonAsync($"/manifests/{Id}/ens", new { mrn = Mrn }, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Gone);
        (await client.DeleteAsync($"/manifests/{Id}/ens", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Gone);
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

        // Deliberately no ENS MRN either. GVMS wants one in sAndSMasterRefNum, which hangs off a
        // declaration container whose primary identifier — a CDS DUCR, a TIR carnet, an ATA carnet —
        // Freedom does not hold. See gotchas 5b.
        properties.Should().NotContain("EnsMrn");
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
    public async Task A_frozen_manifest_cannot_be_edited_reloaded_or_deleted()
    {
        var manifests = new InMemoryManifestRepository(AManifest(ManifestStatus.Confirmed, frozen: true));
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Administrator");
        using var client = api.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        (await client.PutAsJsonAsync($"/manifests/{Id}", new { deliveryNotes = "changed" }, cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await client.DeleteAsync($"/manifests/{Id}", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        manifests.Count.Should().Be(1);
    }

    [Fact]
    public async Task A_frozen_manifest_cannot_be_re_pointed_at_a_different_vehicle()
    {
        // Not because the edit is refused — because there is nowhere to put it. The convoy and
        // the vehicle are the manifest's identity, so PUT /manifests/{id} has no field for them
        // and the UPDATE never names those columns.
        var manifests = new InMemoryManifestRepository(AManifest(ManifestStatus.Preparing));
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/manifests/{Id}",
            new { deliveryNotes = "changed", vin = "WVWZZZ1JZXW999999", convoyId = 99 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        manifests.Manifest(Id)!.Vin.Should().Be(Vin);
        manifests.Manifest(Id)!.ConvoyId.Should().Be(ConvoyId);
        manifests.Manifest(Id)!.DeliveryNotes.Should().Be("changed");
    }

    [Fact]
    public async Task The_manifest_reports_the_crew_travelling_with_its_vehicle()
    {
        // One crew record. The manifest reads it; crewing happens on the truck-list entry.
        var convoys = AConvoy()
            .WithPerson(Primary, "Olena", "Kovalenko")
            .WithPerson(Secondary, "Taras", "Shevchuk")
            .WithCrew(Vin, Primary)
            .WithCrew(Vin, Secondary, CrewRole.Passenger);

        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), convoys, ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var crew = await client.GetFromJsonAsync<JsonElement>(
            $"/manifests/{Id}/crew", TestContext.Current.CancellationToken);

        var members = crew.EnumerateArray().ToList();
        members.Should().HaveCount(2);
        members[0].GetProperty("lastName").GetString().Should().Be("Kovalenko");
        members[0].GetProperty("role").GetString().Should().Be("Driver");
        members[1].GetProperty("role").GetString().Should().Be("Passenger");
    }

    [Fact]
    public async Task There_is_no_way_to_crew_a_manifest_directly()
    {
        // PUT /manifests/{id}/teams is gone. It wrote a second crew record that nothing
        // reconciled with the convoy's, so a printed manifest could name people the insurance —
        // which is what actually gates departure — had never heard of.
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/manifests/{Id}/teams/Uk",
            new { primaryPersonId = Primary, secondaryPersonId = Secondary },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task There_is_no_crew_for_a_manifest_that_does_not_exist()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync("/manifests/MAN-NOPE/crew", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
    public async Task The_border_weight_flags_cargo_over_the_vehicles_stated_capacity_without_rejecting_anything()
    {
        var manifests = new InMemoryManifestRepository(AManifest())
            .WithVehicleWeight(1_400)
            .WithVehicleCargoCapacity(new VehicleCargoCapacityReadModel(20m, null, null, null))
            .WithBoxOn(Id, new ManifestBoxReadModel(1, 30, Validated: true));
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/manifests/{Id}/weight", TestContext.Current.CancellationToken);
        var weight = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Advisory only: still a 200, never a rejection.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        weight.GetProperty("maxCargoWeightKg").GetDecimal().Should().Be(20m);
        weight.GetProperty("cargoOverweight").GetBoolean().Should().BeTrue();
        weight.GetProperty("oversizedBoxIds").EnumerateArray().Should().BeEmpty();
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Writing_cargo_to_a_manifest_is_gone_and_points_at_the_truck_list_entry(string method)
    {
        var manifests = new InMemoryManifestRepository(AManifest());
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), $"/manifests/{Id}/boxes/7"),
            TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        problem.GetProperty("detail").GetString().Should().Contain("/convoys/{id}/vehicles/{vin}/boxes/{boxId}");
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

    // ---- The French logistics envelope -------------------------------------------------------
    //
    // Requested by filing the ELO declaration (plan 08) and obtained by the Customs Worker, so over HTTP
    // the envelope is read-only and its absence is as meaningful as its presence.


    /// <summary>
    /// A manifest that has been approved but whose envelope has not come back yet. Not an error —
    /// the worker drains its queue on a poll loop — but distinguishable from an envelope that exists,
    /// because a dispatcher needs to know before the convoy leaves.
    /// </summary>
    [Fact]
    public async Task A_manifest_with_no_envelope_yet_reports_that_it_has_none()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(),
            roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/manifests/{Id}/elo", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_issued_envelope_reports_its_references_and_its_status()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(),
            AnEnvelopeFor(Id), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/manifests/{Id}/elo", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        envelope.GetProperty("jeton").GetString().Should().Be(Jeton);
        envelope.GetProperty("numeroDossier").GetString().Should().Be(NumeroDossier);
        envelope.GetProperty("statut").GetString().Should().Be("FERMEE");
        envelope.GetProperty("declarationCount").GetInt32().Should().Be(1);
        envelope.GetProperty("hasBarcodeDocument").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// An envelope says nothing about the load, and the JSON is where that has to hold: this is the
    /// shape a browser and any future integration will read.
    /// </summary>
    [Fact]
    public async Task The_envelope_says_nothing_about_where_the_load_is_going()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(),
            AnEnvelopeFor(Id), roles: "Dispatcher");
        using var client = api.CreateClient();

        var envelope = await (await client.GetAsync(
                $"/manifests/{Id}/elo", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        envelope.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "manifestId", "jeton", "numeroDossier", "statut", "declarationCount", "submittedAt",
            "hasBarcodeDocument");
    }

    [Fact]
    public async Task The_barcode_is_served_as_a_pdf_the_driver_can_print()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(),
            AnEnvelopeFor(Id), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync(
            $"/manifests/{Id}/elo/document", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken))
            .Should().Equal(Barcode);
    }

    /// <summary>
    /// The envelope exists at French customs but its barcode did not arrive in a form that could be
    /// stored. The reference is still served — losing it would strand the envelope — and the document
    /// is simply absent.
    /// </summary>
    [Fact]
    public async Task An_envelope_without_a_barcode_still_reports_itself_and_serves_no_document()
    {
        var envelopes = new InMemoryEloEnvelopeStore().With(
            new EloEnvelopeReadModel(Id, Jeton, NumeroDossier, "FERMEE", 1, Submitted, false));
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(),
            envelopes, roles: "Dispatcher");
        using var client = api.CreateClient();

        var envelope = await client.GetAsync($"/manifests/{Id}/elo", TestContext.Current.CancellationToken);
        var document = await client.GetAsync(
            $"/manifests/{Id}/elo/document", TestContext.Current.CancellationToken);

        envelope.StatusCode.Should().Be(HttpStatusCode.OK);
        (await envelope.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("hasBarcodeDocument").GetBoolean().Should().BeFalse();
        document.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reading_an_envelope_without_a_token_is_unauthorized()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(),
            AnEnvelopeFor(Id), authenticated: false);
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/manifests/{Id}/elo", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The Ground Officer is excluded from every manifest policy, and the isolation runs both ways —
    /// a border document is not their business any more than a delivery address is a dispatcher's.
    /// </summary>
    [Fact]
    public async Task A_ground_officer_is_refused_an_envelope_and_its_document()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(),
            AnEnvelopeFor(Id), roles: "GroundOfficer");
        using var client = api.CreateClient();

        var envelope = await client.GetAsync($"/manifests/{Id}/elo", TestContext.Current.CancellationToken);
        var document = await client.GetAsync(
            $"/manifests/{Id}/elo/document", TestContext.Current.CancellationToken);

        envelope.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        document.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Requesting an envelope is a consequence of approval, which is Administrator-only. There is
    /// deliberately no route that submits one on its own.
    /// </summary>
    [Fact]
    public async Task There_is_no_way_to_request_an_envelope_directly()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(),
            roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsync(
            $"/manifests/{Id}/elo", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    private static InMemoryEloEnvelopeStore AnEnvelopeFor(string manifestId) =>
        new InMemoryEloEnvelopeStore().With(
            new EloEnvelopeReadModel(manifestId, Jeton, NumeroDossier, "FERMEE", 1, Submitted, true),
            Barcode);
}
