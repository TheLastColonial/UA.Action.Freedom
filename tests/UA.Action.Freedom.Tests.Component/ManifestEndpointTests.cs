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
        Id, ConvoyId, Vin, status, null, FerryBookingComplete: false,
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

    /// <summary>
    /// A manifest whose ENS has been recorded. Approval refuses without one, so every test that
    /// gets past `propose` has to seed it — the default store is empty, which is what a manifest
    /// looks like before anyone has filed.
    /// </summary>
    private static InMemoryEnsDeclarationStore AnEnsDeclaration() =>
        new InMemoryEnsDeclarationStore().With(Id, Mrn);

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
            declarations: AnEnsDeclaration(), roles: "Administrator");
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
    public async Task Approving_freezes_the_manifest_and_queues_exactly_one_submission()
    {
        var manifests = new InMemoryManifestRepository(AManifest(ManifestStatus.Proposed));
        var queue = new RecordingManifestWorkQueue();
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), queue,
            declarations: AnEnsDeclaration(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsync(
            $"/manifests/{Id}/approve", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        manifests.Manifest(Id)!.Frozen.Should().BeTrue();

        var submission = queue.Submissions.Should().ContainSingle().Subject;
        submission.ManifestId.Should().Be(Id);

        // The plate, not the VIN. It is what a border officer reads off the front of the vehicle,
        // and what HMRC matches the movement against.
        submission.VehicleRegistration.Should().Be("AB12CDE");
        submission.VehicleRegistration.Should().NotBe(Vin);
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
            manifests, AConvoy(), ARosterOfDrivers(), queue,
            declarations: AnEnsDeclaration(), roles: "Administrator");
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
        var manifests = new InMemoryManifestRepository(AManifest(ManifestStatus.Confirmed, frozen: true))
            .WithKnownBox(7);
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Administrator");
        using var client = api.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        (await client.PutAsJsonAsync($"/manifests/{Id}", new { deliveryNotes = "changed" }, cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await client.PutAsync($"/manifests/{Id}/boxes/7", content: null, cancellationToken))
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
        var manifests = new InMemoryManifestRepository(AManifest(ManifestStatus.Preparing))
            .WithKnownBox(7);
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

    // ---- The ICS2 Entry Summary Declaration ---------------------------------------------------
    //
    // Recorded, not submitted. ICS2's Shared Trader Interface speaks eDelivery AS4, and an always-on
    // inbound access point is what recommendations §4.1 declines, so a Ground Officer files in the EU
    // Customs Trader Portal and the MRN is recorded here (docs/adr/0003).

    private static StringContent AnEnsBody(string mrn = Mrn, string filedBy = "groundofficer") =>
        new(
            $$"""
              { "mrn": "{{mrn}}", "acceptedAt": "2026-08-24T09:30:00+00:00",
                "filedBy": "{{filedBy}}", "filingReference": "STP-2026-0001" }
              """,
            System.Text.Encoding.UTF8,
            "application/json");

    [Fact]
    public async Task Recording_a_declaration_keeps_the_json_contract()
    {
        var declarations = new InMemoryEnsDeclarationStore();
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), declarations: declarations, roles: "Dispatcher");
        using var client = api.CreateClient();

        var recorded = await client.PutAsync(
            $"/manifests/{Id}/ens", AnEnsBody(), TestContext.Current.CancellationToken);
        var read = await client.GetAsync($"/manifests/{Id}/ens", TestContext.Current.CancellationToken);

        recorded.StatusCode.Should().Be(HttpStatusCode.Created);

        var declaration = JsonDocument.Parse(
            await read.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

        declaration.GetProperty("manifestId").GetString().Should().Be(Id);
        declaration.GetProperty("mrn").GetString().Should().Be(Mrn);
        declaration.GetProperty("acceptedAt").GetDateTimeOffset()
            .Should().Be(new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero));
        declaration.GetProperty("filedBy").GetString().Should().Be("groundofficer");
        declaration.GetProperty("filingReference").GetString().Should().Be("STP-2026-0001");
    }

    /// <summary>
    /// A declaration says which formality the crossing was accepted under. It says nothing about the
    /// consignment, and the JSON is where that has to be true — the record is stored durably, so a
    /// field added later would persist.
    /// </summary>
    [Fact]
    public async Task A_recorded_declaration_says_nothing_about_where_the_load_is_going()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), declarations: AnEnsDeclaration(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/manifests/{Id}/ens", TestContext.Current.CancellationToken);

        var declaration = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

        declaration.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "manifestId", "mrn", "acceptedAt", "filedBy", "filingReference");
    }

    [Fact]
    public async Task A_manifest_with_no_declaration_yet_reports_that_it_has_none()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/manifests/{Id}/ens", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A malformed MRN is refused at the door. Recorded, it would reach French customs inside an
    /// envelope and come back as FONC-ERR-004 — by which time the manifest is frozen and the convoy
    /// is loading.
    /// </summary>
    [Theory]
    [InlineData("nonsense")]
    [InlineData("25fr17551780961at5")]
    [InlineData("25FR17551780961AT")]
    public async Task A_declaration_that_is_not_shaped_like_an_mrn_is_refused(string mrn)
    {
        var declarations = new InMemoryEnsDeclarationStore();
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), declarations: declarations, roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            $"/manifests/{Id}/ens", AnEnsBody(mrn), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync($"/manifests/{Id}/ens", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Write-once, the same shape as the GMR stamp and the truck-list publication. The envelope names
    /// the MRN, so replacing one silently would leave French customs pairing a crossing against a
    /// formality Freedom no longer believes in.
    /// </summary>
    [Fact]
    public async Task A_second_declaration_is_refused_and_the_first_survives()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), declarations: new InMemoryEnsDeclarationStore(),
            roles: "Dispatcher");
        using var client = api.CreateClient();
        await client.PutAsync($"/manifests/{Id}/ens", AnEnsBody(), TestContext.Current.CancellationToken);

        var second = await client.PutAsync(
            $"/manifests/{Id}/ens", AnEnsBody("26GB99999999999ZZ9"), TestContext.Current.CancellationToken);
        var read = await client.GetAsync($"/manifests/{Id}/ens", TestContext.Current.CancellationToken);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var declaration = JsonDocument.Parse(
            await read.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

        declaration.GetProperty("mrn").GetString().Should().Be(Mrn);
    }

    /// <summary>
    /// Invalidate-and-refile: several ENS fields are non-amendable, so correcting one means
    /// withdrawing the declaration in ICS2 and filing a new one. The withdrawn MRN is kept.
    /// </summary>
    [Fact]
    public async Task A_withdrawn_declaration_makes_room_for_the_refiled_one_and_is_kept()
    {
        var declarations = new InMemoryEnsDeclarationStore();
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), declarations: declarations, roles: "Dispatcher");
        using var client = api.CreateClient();
        await client.PutAsync($"/manifests/{Id}/ens", AnEnsBody(), TestContext.Current.CancellationToken);

        var withdrawn = await client.DeleteAsync(
            $"/manifests/{Id}/ens", TestContext.Current.CancellationToken);
        var refiled = await client.PutAsync(
            $"/manifests/{Id}/ens", AnEnsBody("26GB99999999999ZZ9"), TestContext.Current.CancellationToken);

        withdrawn.StatusCode.Should().Be(HttpStatusCode.NoContent);
        refiled.StatusCode.Should().Be(HttpStatusCode.Created);
        declarations.Superseded.Should().ContainSingle().Which.Mrn.Should().Be(Mrn);
    }

    [Fact]
    public async Task Withdrawing_a_declaration_that_was_never_recorded_is_not_found()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync(
            $"/manifests/{Id}/ens", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The envelope of a frozen manifest already names its declaration, so withdrawing it would
    /// strand the crossing.
    /// </summary>
    [Fact]
    public async Task The_declaration_of_a_frozen_manifest_cannot_be_withdrawn()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest(ManifestStatus.Confirmed, frozen: true)),
            AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(),
            declarations: AnEnsDeclaration(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.DeleteAsync(
            $"/manifests/{Id}/ens", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Recording_a_declaration_against_a_manifest_that_does_not_exist_is_not_found()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), declarations: new InMemoryEnsDeclarationStore(),
            roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            "/manifests/NOSUCH/ens", AnEnsBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// <c>manifests:declare</c> is Administrator and Dispatcher. A Loader may read a declaration
    /// like any other manifest detail and may not record one; a Ground Officer — who is the person
    /// who actually files it — is refused here, because GroundOfficer is excluded from every
    /// manifest policy and that isolation runs both ways.
    /// </summary>
    [Theory]
    [InlineData("Administrator", HttpStatusCode.Created)]
    [InlineData("Dispatcher", HttpStatusCode.Created)]
    [InlineData("Loader", HttpStatusCode.Forbidden)]
    [InlineData("Purchaser", HttpStatusCode.Forbidden)]
    [InlineData("Mechanic", HttpStatusCode.Forbidden)]
    [InlineData("GroundOfficer", HttpStatusCode.Forbidden)]
    public async Task Only_an_administrator_or_dispatcher_may_record_a_declaration(
        string role, HttpStatusCode expected)
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), declarations: new InMemoryEnsDeclarationStore(),
            roles: role);
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            $"/manifests/{Id}/ens", AnEnsBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData("Administrator", HttpStatusCode.NoContent)]
    [InlineData("Dispatcher", HttpStatusCode.NoContent)]
    [InlineData("Loader", HttpStatusCode.Forbidden)]
    [InlineData("GroundOfficer", HttpStatusCode.Forbidden)]
    public async Task Only_an_administrator_or_dispatcher_may_withdraw_a_declaration(
        string role, HttpStatusCode expected)
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), declarations: AnEnsDeclaration(), roles: role);
        using var client = api.CreateClient();

        var response = await client.DeleteAsync(
            $"/manifests/{Id}/ens", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task A_ground_officer_cannot_even_read_a_declaration()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), declarations: AnEnsDeclaration(), roles: "GroundOfficer");
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/manifests/{Id}/ens", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Recording_a_declaration_without_a_token_is_unauthorized()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), authenticated: false);
        using var client = api.CreateClient();

        var response = await client.PutAsync(
            $"/manifests/{Id}/ens", AnEnsBody(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The filing sheet over HTTP. It is a read of the manifest, so every operational role may see it
    /// — the person filing needs it, and so does the dispatcher who has to close its gaps.
    /// </summary>
    [Fact]
    public async Task The_filing_sheet_keeps_the_json_contract()
    {
        var manifests = new InMemoryManifestRepository(AManifest())
            .WithVehicleWeight(1_400)
            .WithBoxOn(Id, new ManifestBoxReadModel(1, 30, Validated: true));
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Loader");
        using var client = api.CreateClient();

        var response = await client.GetAsync(
            $"/manifests/{Id}/ens/filing-sheet", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var sheet = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

        sheet.GetProperty("manifestId").GetString().Should().Be(Id);
        sheet.GetProperty("modeOfTransportCode").GetInt32().Should().Be(EnsTransportMode.Maritime);
        sheet.GetProperty("passiveMeansOfTransport").GetString().Should().Be("AB12CDE");
        sheet.GetProperty("grossMassKg").GetInt32().Should().Be(ManifestWeight.Total(1_400, 30));
        sheet.GetProperty("consigneeAddressSource").GetString().Should().Be("GET /receivers/{ref}/detail");

        var consignment = sheet.GetProperty("consignments").EnumerateArray().Should().ContainSingle().Subject;
        consignment.GetProperty("packageTypeCode").GetString().Should().Be(EnsPackaging.Box);
        consignment.GetProperty("consigneeAddressWithheld").GetBoolean().Should().BeTrue();
        consignment.GetProperty("goodsItems").EnumerateArray().Should().ContainSingle();
    }

    /// <summary>
    /// The redaction, asserted on the serialised body rather than the type — the sheet is what a
    /// filer reads, and a Ukrainian address on it would be a targeting document. It says where to get
    /// the address instead, so a filer cannot conclude there is none.
    /// </summary>
    [Fact]
    public async Task The_filing_sheet_says_nothing_about_where_the_load_is_going()
    {
        var manifests = new InMemoryManifestRepository(AManifest())
            .WithBoxOn(Id, new ManifestBoxReadModel(1, 30, Validated: true));
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync(
            $"/manifests/{Id}/ens/filing-sheet", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        foreach (var forbidden in new[] { "street", "postcode", "addressLine", "contactPhone", "contactName" })
        {
            body.Should().NotContainEquivalentOf(forbidden);
        }
    }

    [Fact]
    public async Task A_filing_sheet_for_a_manifest_that_does_not_exist_is_not_found()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync(
            "/manifests/NOSUCH/ens/filing-sheet", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A manifest with no cargo cannot be declared, and the sheet says so rather than returning an
    /// empty shell that looks filable.
    /// </summary>
    [Fact]
    public async Task The_filing_sheet_reports_what_is_still_missing()
    {
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), roles: "Dispatcher");
        using var client = api.CreateClient();

        var response = await client.GetAsync(
            $"/manifests/{Id}/ens/filing-sheet", TestContext.Current.CancellationToken);

        var sheet = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

        sheet.GetProperty("complete").GetBoolean().Should().BeFalse();
        sheet.GetProperty("missing").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_ground_officer_cannot_read_a_filing_sheet()
    {
        // The one role that actually files the declaration is refused the sheet, because
        // GroundOfficer is excluded from every manifest policy and the isolation runs both ways. The
        // sheet reaches them through a Dispatcher, which is the same hand-off the MRN comes back on.
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), AConvoy(), ARosterOfDrivers(),
            new RecordingManifestWorkQueue(), roles: "GroundOfficer");
        using var client = api.CreateClient();

        var response = await client.GetAsync(
            $"/manifests/{Id}/ens/filing-sheet", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- The French logistics envelope -------------------------------------------------------
    //
    // Requested by approving the manifest, the same way a GMR is, and obtained by the Customs
    // Worker — so over HTTP it is read-only, and its absence is as meaningful as its presence.

    [Fact]
    public async Task Approving_a_manifest_asks_for_a_french_logistics_envelope()
    {
        var queue = new RecordingManifestWorkQueue();
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest(ManifestStatus.Proposed)), AConvoy(), ARosterOfDrivers(), queue,
            declarations: AnEnsDeclaration(), roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsync(
            $"/manifests/{Id}/approve", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var envelope = queue.Envelopes.Should().ContainSingle().Subject;
        envelope.ManifestId.Should().Be(Id);
        envelope.Profile.Should().Be(EloCrossingProfile.HumanitarianAidToUkraine);

        // The point of the ICS2 integration: the envelope names the declaration the crossing was
        // accepted under, not the placeholder identifier real French customs refuses.
        envelope.DeclarationIdentifiers.Should().Equal(Mrn);
    }

    /// <summary>
    /// The gate that makes the whole slice worth having. France pairs the crossing against the ENS at
    /// the Smart Border, so a manifest with none is refused — and refused before anything is frozen,
    /// so it can still be approved once the MRN arrives.
    /// </summary>
    [Fact]
    public async Task Approving_a_manifest_with_no_declaration_is_refused_and_freezes_nothing()
    {
        var manifests = new InMemoryManifestRepository(AManifest(ManifestStatus.Proposed));
        var queue = new RecordingManifestWorkQueue();
        await using var api = FreedomApi.WithManifests(
            manifests, AConvoy(), ARosterOfDrivers(), queue, roles: "Administrator");
        using var client = api.CreateClient();

        var response = await client.PostAsync(
            $"/manifests/{Id}/approve", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        manifests.Manifest(Id)!.Frozen.Should().BeFalse();
        manifests.Manifest(Id)!.Status.Should().Be(ManifestStatus.Proposed);
        queue.Envelopes.Should().BeEmpty();
        queue.Submissions.Should().BeEmpty();
        queue.Documents.Should().BeEmpty();

        // The refusal has to be actionable: a dispatcher reading it must learn what to do next.
        var problem = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        problem.Should().Contain("/manifests/{id}/ens");
    }

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
