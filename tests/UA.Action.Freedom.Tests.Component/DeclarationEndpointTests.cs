using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Declarations;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// A vehicle's customs declarations from the outside (ADR 0005, ADR 0006): recorded by hand by default,
/// filed through a worker only where an authority's mode says so, ELO only after an accepted ENS.
/// </summary>
public class DeclarationEndpointTests
{
    private const string Id = "MAN-0001";
    private const int ConvoyId = 42;
    private const string Vin = "WVWZZZ1JZXW000001";
    private const string Mrn = "25FR17551780961AT5";
    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Published = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Stamped = new(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

    private static readonly string Base = $"/convoys/{ConvoyId}/vehicles/{Vin}/declarations";

    private static ManifestReadModel AManifest(bool approved = true) => new(
        Id, ConvoyId, Vin, approved ? ManifestStatus.Confirmed : ManifestStatus.Proposed, null,
        GmrSubmittedAt: approved ? Stamped : null);

    private static InMemoryConvoyRepository AConvoy() =>
        new InMemoryConvoyRepository(new ConvoyReadModel(ConvoyId, Departs, Departs.AddDays(4), Published))
            .WithVehicle(Vin, onConvoy: ConvoyId);

    private static InMemoryPersonRepository ARoster() =>
        new InMemoryPersonRepository().CalledByALinkedVolunteer();

    private static StringContent AnEnsBody(string mrn = Mrn) =>
        new(
            $$"""
              { "mrn": "{{mrn}}", "acceptedAt": "2026-08-24T09:30:00+00:00",
                "filedBy": "groundofficer", "filingReference": "STP-2026-0001" }
              """,
            System.Text.Encoding.UTF8,
            "application/json");

    private static (WebApplicationFactoryHolder Api, InMemoryDeclarationRepository Declarations, RecordingManifestWorkQueue Queue)
        ApiFor(
            string role,
            bool approved = true,
            DeclarationSubmissionModes? modes = null,
            Func<InMemoryDeclarationRepository, InMemoryDeclarationRepository>? seed = null,
            InMemoryManifestRepository? manifests = null)
    {
        var convoys = AConvoy();
        var declarations = seed?.Invoke(new InMemoryDeclarationRepository(convoys))
                           ?? new InMemoryDeclarationRepository(convoys);
        var queue = new RecordingManifestWorkQueue();
        var api = FreedomApi.WithManifests(
            manifests ?? new InMemoryManifestRepository(AManifest(approved))
                .WithBoxOn(Id, new ManifestBoxReadModel(1, 30, Validated: true)),
            convoys, ARoster(), queue,
            declarations: declarations, submissionModes: modes, roles: role);

        return (new WebApplicationFactoryHolder(api), declarations, queue);
    }

    // ---- reading ----------------------------------------------------------------------------------

    [Fact]
    public async Task Reading_declarations_without_a_token_is_unauthorized()
    {
        var convoys = AConvoy();
        await using var api = FreedomApi.WithManifests(
            new InMemoryManifestRepository(AManifest()), convoys, ARoster(),
            new RecordingManifestWorkQueue(), authenticated: false);
        using var client = api.CreateClient();

        var response = await client.GetAsync(Base, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_ground_officer_is_refused_declarations()
    {
        var (holder, _, _) = ApiFor("GroundOfficer");
        await using var api = holder.Api;
        using var client = api.CreateClient();

        var response = await client.GetAsync(Base, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Listing_keeps_the_json_contract_and_carries_no_authority_text()
    {
        var (holder, _, _) = ApiFor("Dispatcher");
        await using var api = holder.Api;
        using var client = api.CreateClient();
        await client.PostAsJsonAsync(
            $"{Base}/gmr/record", new { reference = "GMR-1" }, TestContext.Current.CancellationToken);

        var response = await client.GetAsync(Base, TestContext.Current.CancellationToken);

        var list = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        var gmr = list.EnumerateArray().Should().ContainSingle().Subject;
        gmr.GetProperty("kind").GetString().Should().Be("Gmr");
        gmr.GetProperty("status").GetString().Should().Be("Filed");
        gmr.GetProperty("reference").GetString().Should().Be("GMR-1");
        gmr.GetProperty("vin").GetString().Should().Be(Vin);
        gmr.EnumerateObject().Select(property => property.Name).Should().NotContain(
            ["libelleErreur", "authorityMessage", "message"]);
    }

    [Fact]
    public async Task Listing_the_declarations_of_a_vehicle_that_is_not_on_the_convoy_is_not_found()
    {
        var (holder, _, _) = ApiFor("Dispatcher");
        await using var api = holder.Api;
        using var client = api.CreateClient();

        var response = await client.GetAsync(
            $"/convoys/{ConvoyId}/vehicles/NOSUCHVIN000000/declarations", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- manual: record ---------------------------------------------------------------------------

    [Fact]
    public async Task A_dispatcher_records_the_gmr_reference_and_it_is_filed()
    {
        var (holder, declarations, queue) = ApiFor("Dispatcher");
        await using var api = holder.Api;
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"{Base}/gmr/record", new { reference = "GMR-1" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        declarations.All.Should().ContainSingle().Which.Status.Should().Be(DeclarationStatus.Filed);
        queue.Submissions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_recorded_reference_is_write_once()
    {
        var (holder, declarations, _) = ApiFor("Dispatcher");
        await using var api = holder.Api;
        using var client = api.CreateClient();
        await client.PostAsJsonAsync(
            $"{Base}/gmr/record", new { reference = "GMR-1" }, TestContext.Current.CancellationToken);

        var second = await client.PostAsJsonAsync(
            $"{Base}/gmr/record", new { reference = "GMR-2" }, TestContext.Current.CancellationToken);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        declarations.All.Should().ContainSingle().Which.Reference.Should().Be("GMR-1");
    }

    [Fact]
    public async Task A_goods_list_is_recorded_per_receiver_and_needs_one()
    {
        var (holder, declarations, _) = ApiFor("Dispatcher");
        await using var api = holder.Api;
        using var client = api.CreateClient();
        var receiver = Guid.NewGuid();

        var without = await client.PostAsJsonAsync(
            $"{Base}/goods-list/record", new { reference = "UA-1" }, TestContext.Current.CancellationToken);
        var first = await client.PostAsJsonAsync(
            $"{Base}/goods-list/record", new { reference = "UA-1", receiverRef = receiver },
            TestContext.Current.CancellationToken);
        var second = await client.PostAsJsonAsync(
            $"{Base}/goods-list/record", new { reference = "UA-2", receiverRef = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        without.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
        declarations.All.Should().HaveCount(2);
    }

    [Fact]
    public async Task An_unknown_kind_is_a_bad_request()
    {
        var (holder, _, _) = ApiFor("Dispatcher");
        await using var api = holder.Api;
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"{Base}/passport/record", new { reference = "X" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("Loader")]
    [InlineData("Mechanic")]
    [InlineData("Purchaser")]
    [InlineData("GroundOfficer")]
    public async Task Only_an_administrator_or_dispatcher_may_record_a_declaration(string role)
    {
        var (holder, _, _) = ApiFor(role);
        await using var api = holder.Api;
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"{Base}/gmr/record", new { reference = "GMR-1" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- refused ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_filed_declaration_is_refused_with_a_bounded_code_and_can_then_be_recorded_afresh()
    {
        var (holder, declarations, _) = ApiFor("Dispatcher");
        await using var api = holder.Api;
        using var client = api.CreateClient();
        await client.PostAsJsonAsync(
            $"{Base}/gmr/record", new { reference = "GMR-1" }, TestContext.Current.CancellationToken);

        var refused = await client.PostAsJsonAsync(
            $"{Base}/gmr/refused", new { reasonCode = "data-error" }, TestContext.Current.CancellationToken);
        var corrected = await client.PostAsJsonAsync(
            $"{Base}/gmr/record", new { reference = "GMR-2" }, TestContext.Current.CancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.NoContent);
        corrected.StatusCode.Should().Be(HttpStatusCode.NoContent);
        declarations.All.Should().ContainSingle().Which.Reference.Should().Be("GMR-2");
    }

    [Fact]
    public async Task A_refusal_reason_that_is_not_a_known_code_is_rejected_so_authority_text_is_never_stored()
    {
        var (holder, _, _) = ApiFor("Dispatcher");
        await using var api = holder.Api;
        using var client = api.CreateClient();
        await client.PostAsJsonAsync(
            $"{Base}/gmr/record", new { reference = "GMR-1" }, TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync(
            $"{Base}/gmr/refused", new { reasonCode = "Consignee at 12 Vulytsia Sumska unknown" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- ENS --------------------------------------------------------------------------------------

    [Fact]
    public async Task Recording_an_ens_keeps_the_json_contract_and_is_accepted_straight_away()
    {
        var (holder, declarations, _) = ApiFor("Dispatcher");
        await using var api = holder.Api;
        using var client = api.CreateClient();

        var recorded = await client.PutAsync($"{Base}/ens", AnEnsBody(), TestContext.Current.CancellationToken);
        var read = await client.GetAsync($"{Base}/ens", TestContext.Current.CancellationToken);

        recorded.StatusCode.Should().Be(HttpStatusCode.Created);
        declarations.All.Should().ContainSingle().Which.Status.Should().Be(DeclarationStatus.Accepted);

        var ens = JsonDocument.Parse(
            await read.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        ens.GetProperty("mrn").GetString().Should().Be(Mrn);
        ens.GetProperty("acceptedAt").GetDateTimeOffset()
            .Should().Be(new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero));
        ens.GetProperty("filedBy").GetString().Should().Be("groundofficer");
        ens.GetProperty("filingReference").GetString().Should().Be("STP-2026-0001");
        ens.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "declarationId", "mrn", "acceptedAt", "filedBy", "filingReference");
    }

    [Fact]
    public async Task An_ens_can_be_recorded_before_the_load_is_signed_off()
    {
        // Declarations hang off the truck-list entry, not the manifest, and approval no longer needs one.
        var (holder, _, _) = ApiFor("Dispatcher", approved: false);
        await using var api = holder.Api;
        using var client = api.CreateClient();

        var recorded = await client.PutAsync($"{Base}/ens", AnEnsBody(), TestContext.Current.CancellationToken);

        recorded.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("25fr17551780961at5")]
    public async Task A_malformed_mrn_is_a_bad_request_and_records_nothing(string mrn)
    {
        var (holder, declarations, _) = ApiFor("Dispatcher");
        await using var api = holder.Api;
        using var client = api.CreateClient();

        var response = await client.PutAsync($"{Base}/ens", AnEnsBody(mrn), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        declarations.All.Should().BeEmpty();
    }

    [Fact]
    public async Task A_second_ens_is_refused_until_the_first_is_withdrawn_and_the_withdrawn_one_is_kept()
    {
        var (holder, declarations, _) = ApiFor("Dispatcher");
        await using var api = holder.Api;
        using var client = api.CreateClient();
        await client.PutAsync($"{Base}/ens", AnEnsBody(), TestContext.Current.CancellationToken);

        var refused = await client.PutAsync(
            $"{Base}/ens", AnEnsBody("26GB99999999999ZZ9"), TestContext.Current.CancellationToken);
        var withdrawn = await client.DeleteAsync($"{Base}/ens", TestContext.Current.CancellationToken);
        var refiled = await client.PutAsync(
            $"{Base}/ens", AnEnsBody("26GB99999999999ZZ9"), TestContext.Current.CancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        withdrawn.StatusCode.Should().Be(HttpStatusCode.NoContent);
        refiled.StatusCode.Should().Be(HttpStatusCode.Created);
        declarations.All.Should().HaveCount(2);
        declarations.All.Should().ContainSingle(row => row.Status == DeclarationStatus.Withdrawn)
            .Which.Reference.Should().Be(Mrn);
    }

    [Fact]
    public async Task Reading_or_withdrawing_an_ens_that_was_never_recorded_is_not_found()
    {
        var (holder, _, _) = ApiFor("Dispatcher");
        await using var api = holder.Api;
        using var client = api.CreateClient();

        (await client.GetAsync($"{Base}/ens", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"{Base}/ens", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- ELO needs an accepted ENS ----------------------------------------------------------------

    [Fact]
    public async Task An_elo_cannot_be_recorded_without_an_accepted_ens_and_can_once_there_is_one()
    {
        var (holder, _, _) = ApiFor("Dispatcher");
        await using var api = holder.Api;
        using var client = api.CreateClient();

        var before = await client.PostAsJsonAsync(
            $"{Base}/elo/record", new { reference = "ELO-1" }, TestContext.Current.CancellationToken);
        await client.PutAsync($"{Base}/ens", AnEnsBody(), TestContext.Current.CancellationToken);
        var after = await client.PostAsJsonAsync(
            $"{Base}/elo/record", new { reference = "ELO-1" }, TestContext.Current.CancellationToken);

        before.StatusCode.Should().Be(HttpStatusCode.Conflict);
        after.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---- automatic: file --------------------------------------------------------------------------

    [Fact]
    public async Task In_manual_mode_filing_enqueues_nothing_and_says_to_record_the_reference()
    {
        var (holder, _, queue) = ApiFor("Dispatcher");
        await using var api = holder.Api;
        using var client = api.CreateClient();

        var response = await client.PostAsync($"{Base}/gmr/file", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("record");
        queue.Submissions.Should().BeEmpty();
    }

    [Fact]
    public async Task In_automatic_mode_filing_the_gmr_enqueues_it_with_the_plate_and_marks_it_filed()
    {
        var (holder, declarations, queue) = ApiFor(
            "Dispatcher", modes: new DeclarationSubmissionModes(Gmr: SubmissionMode.Automatic));
        await using var api = holder.Api;
        using var client = api.CreateClient();

        var response = await client.PostAsync($"{Base}/gmr/file", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var submission = queue.Submissions.Should().ContainSingle().Subject;
        submission.ManifestId.Should().Be(Id);
        submission.VehicleRegistration.Should().Be("AB12CDE").And.NotBe(Vin);
        submission.DepartsAt.Should().Be(Departs);
        declarations.All.Should().ContainSingle().Which.Status.Should().Be(DeclarationStatus.Filed);

        var again = await client.PostAsync($"{Base}/gmr/file", null, TestContext.Current.CancellationToken);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        queue.Submissions.Should().ContainSingle();
    }

    [Fact]
    public async Task Filing_the_elo_names_the_ens_mrn_and_is_refused_without_one()
    {
        var (holder, _, queue) = ApiFor(
            "Dispatcher", modes: new DeclarationSubmissionModes(Elo: SubmissionMode.Automatic));
        await using var api = holder.Api;
        using var client = api.CreateClient();

        var without = await client.PostAsync($"{Base}/elo/file", null, TestContext.Current.CancellationToken);
        await client.PutAsync($"{Base}/ens", AnEnsBody(), TestContext.Current.CancellationToken);
        var with = await client.PostAsync($"{Base}/elo/file", null, TestContext.Current.CancellationToken);

        without.StatusCode.Should().Be(HttpStatusCode.Conflict);
        with.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var envelope = queue.Envelopes.Should().ContainSingle().Subject;
        envelope.Profile.Should().Be(EloCrossingProfile.HumanitarianAidToUkraine);
        envelope.DeclarationIdentifiers.Should().Equal(Mrn);
    }

    [Fact]
    public async Task Filing_waits_for_the_load_to_be_signed_off()
    {
        var (holder, _, queue) = ApiFor(
            "Dispatcher", approved: false, modes: new DeclarationSubmissionModes(Gmr: SubmissionMode.Automatic));
        await using var api = holder.Api;
        using var client = api.CreateClient();

        var response = await client.PostAsync($"{Base}/gmr/file", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        queue.Submissions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ens")]
    [InlineData("goods-list")]
    public async Task The_ens_and_the_goods_list_can_never_be_filed_automatically(string kind)
    {
        var (holder, _, _) = ApiFor(
            "Dispatcher", modes: new DeclarationSubmissionModes(SubmissionMode.Automatic, SubmissionMode.Automatic));
        await using var api = holder.Api;
        using var client = api.CreateClient();

        var response = await client.PostAsync($"{Base}/{kind}/file", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- the filing sheet -------------------------------------------------------------------------

    [Fact]
    public async Task The_filing_sheet_keeps_the_json_contract_and_names_who_enters_the_address()
    {
        var (holder, _, _) = ApiFor("Loader");
        await using var api = holder.Api;
        using var client = api.CreateClient();

        var response = await client.GetAsync($"{Base}/ens/filing-sheet", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sheet = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        sheet.GetProperty("modeOfTransportCode").GetInt32().Should().Be(EnsTransportMode.Maritime);
        sheet.GetProperty("consigneeAddressSource").GetString()
            .Should().Be("Entered by the Ground Officer in the portal");
        sheet.GetProperty("consignments").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("consigneeAddressWithheld").GetBoolean().Should().BeTrue();
    }

    /// <summary>The address must never leak: asserted on the serialised body, after every change to the sheet.</summary>
    [Fact]
    public async Task The_filing_sheet_says_nothing_about_where_the_load_is_going()
    {
        var (holder, _, _) = ApiFor("Dispatcher");
        await using var api = holder.Api;
        using var client = api.CreateClient();

        var body = await client.GetStringAsync($"{Base}/ens/filing-sheet", TestContext.Current.CancellationToken);

        foreach (var forbidden in new[] { "street", "postcode", "addressLine", "contactPhone", "contactName" })
        {
            body.Should().NotContainEquivalentOf(forbidden);
        }
    }

    [Fact]
    public async Task A_ground_officer_cannot_read_a_filing_sheet_and_a_vehicle_with_no_manifest_has_none()
    {
        var (officer, _, _) = ApiFor("GroundOfficer");
        await using var officerApi = officer.Api;
        using var officerClient = officerApi.CreateClient();
        var (dispatcher, _, _) = ApiFor("Dispatcher", manifests: new InMemoryManifestRepository());
        await using var dispatcherApi = dispatcher.Api;
        using var dispatcherClient = dispatcherApi.CreateClient();

        (await officerClient.GetAsync($"{Base}/ens/filing-sheet", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await dispatcherClient.GetAsync($"{Base}/ens/filing-sheet", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Holds the factory so the helper can return a tuple while the test still disposes it.</summary>
    internal sealed record WebApplicationFactoryHolder(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Api);
}
