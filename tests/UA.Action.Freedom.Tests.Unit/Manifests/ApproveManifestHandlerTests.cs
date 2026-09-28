using AwesomeAssertions;
using MELT;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Manifests;

/// <summary>
/// Approval — the fork in docs/process.puml, and the moment a manifest stops being editable.
/// </summary>
/// <remarks>
/// Approving confirms the manifest, freezes it, and hands its Goods Movement Reference to the
/// customs worker. The ordering of those is the interesting part: getting it wrong either loses
/// a convoy's GMR or, worse, leaves a manifest editable after HMRC has been told what is in it.
/// </remarks>
public class ApproveManifestHandlerTests
{
    private const string Id = "MAN-0001";

    private static readonly DateTime Stamped = new(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    /// <summary>HMRC needs a crossing time, and the convoy is what knows it.</summary>
    private static IConvoyRepository AConvoy()
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(42, Arg.Any<CancellationToken>()).Returns(
            new ConvoyReadModel(42, Departs, Departs.AddDays(4), new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc)));
        return convoys;
    }

    private const string Vin = "WVWZZZ1JZXW000001";

    /// <summary>What is on the front of the vehicle — not the VIN, which is the chassis number.</summary>
    private const string Plate = "AB12 CDE";

    private static ManifestReadModel AManifest(
        ManifestStatus status = ManifestStatus.Proposed, bool frozen = false) => new(
        Id, 42, Vin, status, null, FerryBookingComplete: false,
        GmrSubmittedAt: frozen ? Stamped : null);

    /// <summary>The Movement Reference Number ICS2 issued for the crossing.</summary>
    private const string Mrn = "25FR17551780961AT5";

    /// <summary>
    /// A manifest whose ENS has been recorded, which is the only kind that may be approved: under
    /// ENV_CTR_RG08 a TIR/ATA lorry's envelope must name exactly one formality, and this is it.
    /// </summary>
    private static IEnsDeclarationStore AnEnsDeclaration()
    {
        var declarations = Substitute.For<IEnsDeclarationStore>();
        declarations.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(
            new EnsDeclarationReadModel(
                Id, Mrn, new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero), "groundofficer", null));
        return declarations;
    }

    /// <summary>A manifest nobody has filed an ENS for yet.</summary>
    private static IEnsDeclarationStore NoEnsDeclaration() => Substitute.For<IEnsDeclarationStore>();

    [Fact]
    public async Task Confirms_the_manifest_and_queues_its_goods_movement_record()
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(AManifest());
        repository.ConfirmAndFreezeAsync(Id, ManifestStatus.Proposed, Arg.Any<CancellationToken>()).Returns(Stamped);
        repository.GetVehiclePlateAsync(Id, Arg.Any<CancellationToken>()).Returns(Plate);
        var queue = Substitute.For<IManifestWorkQueue>();
        var handler = new ApproveManifestHandler(repository, AConvoy(), queue, AnEnsDeclaration());

        var outcome = await handler.HandleAsync(new ApproveManifestCommand(Id), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.Transitioned);
        await queue.Received(1).EnqueueGmrSubmissionAsync(
            Arg.Is<GmrSubmissionRequest>(request =>
                request.ManifestId == Id
                && request.VehicleRegistration == Plate
                && request.DepartsAt == Departs),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tells_HMRC_the_registration_plate_rather_than_the_VIN()
    {
        // GmrSubmissionRequest.VehicleRegistration says "the plate the border expects to see", and
        // was being handed Manifest.Vin — the chassis number, which is not on the front of the
        // vehicle and is not what a border officer matches against the movement.
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(AManifest());
        repository.ConfirmAndFreezeAsync(Id, ManifestStatus.Proposed, Arg.Any<CancellationToken>()).Returns(Stamped);
        repository.GetVehiclePlateAsync(Id, Arg.Any<CancellationToken>()).Returns(Plate);
        var queue = Substitute.For<IManifestWorkQueue>();

        await new ApproveManifestHandler(repository, AConvoy(), queue, AnEnsDeclaration()).HandleAsync(
            new ApproveManifestCommand(Id), TestContext.Current.CancellationToken);

        await queue.Received(1).EnqueueGmrSubmissionAsync(
            Arg.Is<GmrSubmissionRequest>(request => request.VehicleRegistration != Vin),
            Arg.Any<CancellationToken>());
        await queue.Received(1).EnqueueDocumentAsync(
            Arg.Is<ManifestDocumentRequest>(document => document.VehicleRegistration == Plate),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Freezes_the_manifest_before_the_submission_is_queued()
    {
        // The order is load-bearing. If the enqueue fails afterwards the manifest is frozen with
        // no GMR — visible, and retryable by an operator. The other order risks a manifest that
        // is still editable while its GMR is already on its way, which §5.2 rules out.
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(AManifest());
        repository.ConfirmAndFreezeAsync(Id, ManifestStatus.Proposed, Arg.Any<CancellationToken>()).Returns(Stamped);
        var queue = Substitute.For<IManifestWorkQueue>();
        var handler = new ApproveManifestHandler(repository, AConvoy(), queue, AnEnsDeclaration());

        await handler.HandleAsync(new ApproveManifestCommand(Id), CancellationToken.None);

        Received.InOrder(() =>
        {
            repository.ConfirmAndFreezeAsync(Id, ManifestStatus.Proposed, Arg.Any<CancellationToken>());
            queue.EnqueueGmrSubmissionAsync(Arg.Any<GmrSubmissionRequest>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Refuses_to_approve_a_manifest_that_was_never_proposed()
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(AManifest(ManifestStatus.Created));
        var queue = Substitute.For<IManifestWorkQueue>();
        var handler = new ApproveManifestHandler(repository, AConvoy(), queue, AnEnsDeclaration());

        var outcome = await handler.HandleAsync(new ApproveManifestCommand(Id), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.IllegalTransition);
        await queue.DidNotReceive().EnqueueGmrSubmissionAsync(
            Arg.Any<GmrSubmissionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_to_approve_a_manifest_twice_and_does_not_queue_a_second_record()
    {
        // A duplicate GMR for one vehicle is a mess at the border, so the freeze guards the
        // queue as well as the data.
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>())
            .Returns(AManifest(ManifestStatus.Confirmed, frozen: true));
        var queue = Substitute.For<IManifestWorkQueue>();
        var handler = new ApproveManifestHandler(repository, AConvoy(), queue, AnEnsDeclaration());

        var outcome = await handler.HandleAsync(new ApproveManifestCommand(Id), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.Frozen);
        await queue.DidNotReceive().EnqueueGmrSubmissionAsync(
            Arg.Any<GmrSubmissionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Queues_nothing_when_the_manifest_moved_underneath_us()
    {
        // The conditional confirm found nothing, so somebody else already approved it.
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(AManifest());
        repository.ConfirmAndFreezeAsync(Id, ManifestStatus.Proposed, Arg.Any<CancellationToken>())
            .Returns((DateTime?)null);
        var queue = Substitute.For<IManifestWorkQueue>();
        var handler = new ApproveManifestHandler(repository, AConvoy(), queue, AnEnsDeclaration());

        var outcome = await handler.HandleAsync(new ApproveManifestCommand(Id), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.IllegalTransition);
        await queue.DidNotReceive().EnqueueGmrSubmissionAsync(
            Arg.Any<GmrSubmissionRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The other prong of the fork in docs/process.puml: Generate GMR and Generate ELO run in
    /// parallel off approval. France requires a logistics envelope per transport unit at the Smart
    /// Border, so a convoy without one does not sail.
    /// </summary>
    [Fact]
    public async Task Queues_the_french_logistics_envelope_alongside_the_goods_movement_record()
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(AManifest());
        repository.ConfirmAndFreezeAsync(Id, ManifestStatus.Proposed, Arg.Any<CancellationToken>()).Returns(Stamped);
        var queue = Substitute.For<IManifestWorkQueue>();
        var handler = new ApproveManifestHandler(repository, AConvoy(), queue, AnEnsDeclaration());

        await handler.HandleAsync(new ApproveManifestCommand(Id), TestContext.Current.CancellationToken);

        await queue.Received(1).EnqueueEloEnvelopeAsync(
            Arg.Is<EloEnvelopeRequest>(request =>
                request.ManifestId == Id
                && request.Profile == EloCrossingProfile.HumanitarianAidToUkraine),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An envelope describes a crossing, not a load. There is nowhere on the request to put a
    /// receiver, an address or a box, and this pins that rather than trusting it: the request is
    /// serialised onto a durable queue, and a field added here would travel.
    /// </summary>
    /// <remarks>
    /// <c>DeclarationIdentifiers</c> joined the record when ICS2 landed, exactly as the record's own
    /// remarks predicted. It carries MRNs — references to formalities other systems issued — which is
    /// the one kind of consignment-adjacent data an envelope has always held.
    /// </remarks>
    [Fact]
    public void The_envelope_request_has_nowhere_to_put_a_delivery_detail()
    {
        typeof(EloEnvelopeRequest).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo("ManifestId", "Profile", "DeclarationIdentifiers");
    }

    /// <summary>
    /// The point of the whole ICS2 integration. Until an ENS existed the envelope was given
    /// <c>Elo:PlaceholderDeclarationIdentifier</c>, which the local stub accepted and real French
    /// customs refuses with FONC-ERR-004. Now it carries the MRN the crossing was accepted under.
    /// </summary>
    [Fact]
    public async Task Hands_the_real_declaration_identifier_to_the_envelope()
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(AManifest());
        repository.ConfirmAndFreezeAsync(Id, ManifestStatus.Proposed, Arg.Any<CancellationToken>()).Returns(Stamped);
        var queue = Substitute.For<IManifestWorkQueue>();
        var handler = new ApproveManifestHandler(repository, AConvoy(), queue, AnEnsDeclaration());

        await handler.HandleAsync(new ApproveManifestCommand(Id), TestContext.Current.CancellationToken);

        await queue.Received(1).EnqueueEloEnvelopeAsync(
            Arg.Is<EloEnvelopeRequest>(request =>
                request.DeclarationIdentifiers.Count == 1 && request.DeclarationIdentifiers[0] == Mrn),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The ENS MRN does not reach HMRC, and that is a finding rather than an omission.
    /// </summary>
    /// <remarks>
    /// GVMS has the right field — <c>sAndSMasterRefNum</c>, for a Safety and Security declaration's
    /// MRN, which is what an ENS is — but it hangs off a declaration container, and every container
    /// requires a primary identifier Freedom does not hold: a CDS DUCR, a TIR carnet number or an ATA
    /// carnet number. Putting the MRN in <c>customsDeclarationId</c> would file an ICS2 reference as a
    /// CDS one, and the spec scopes ICS2 MRNs to GB_TO_NI while these movements are UK_OUTBOUND.
    /// Pinned as a property-set assertion so adding the field is a deliberate act with this note
    /// attached (docs/gotchas-and-open-questions.md 5b).
    /// </remarks>
    [Fact]
    public void The_goods_movement_record_has_nowhere_to_put_the_declaration()
    {
        typeof(GmrSubmissionRequest).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo("ManifestId", "VehicleRegistration", "DepartsAt");
    }

    /// <summary>
    /// Approval is refused outright when no ENS has been recorded, and refused <em>before</em> the
    /// freeze. This is the ordering that matters most in the whole slice: the alternative is a
    /// manifest frozen for ever against an envelope French customs will never issue, which is what
    /// the placeholder setting used to produce.
    /// </summary>
    [Fact]
    public async Task Refuses_to_approve_a_manifest_with_no_entry_summary_declaration()
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(AManifest());
        var queue = Substitute.For<IManifestWorkQueue>();
        var handler = new ApproveManifestHandler(repository, AConvoy(), queue, NoEnsDeclaration());

        var outcome = await handler.HandleAsync(
            new ApproveManifestCommand(Id), TestContext.Current.CancellationToken);

        outcome.Should().Be(TransitionManifestOutcome.EnsNotFiled);
        await repository.DidNotReceive().ConfirmAndFreezeAsync(
            Arg.Any<string>(), Arg.Any<ManifestStatus>(), Arg.Any<CancellationToken>());
        await queue.DidNotReceive().EnqueueGmrSubmissionAsync(
            Arg.Any<GmrSubmissionRequest>(), Arg.Any<CancellationToken>());
        await queue.DidNotReceive().EnqueueEloEnvelopeAsync(
            Arg.Any<EloEnvelopeRequest>(), Arg.Any<CancellationToken>());
        await queue.DidNotReceive().EnqueueDocumentAsync(
            Arg.Any<ManifestDocumentRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The declaration is read before anything is written, so a manifest refused for want of one is
    /// left exactly as it was and can be approved again once the MRN arrives.
    /// </summary>
    [Fact]
    public async Task Reads_the_declaration_before_it_freezes_anything()
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(AManifest());
        repository.ConfirmAndFreezeAsync(Id, ManifestStatus.Proposed, Arg.Any<CancellationToken>()).Returns(Stamped);
        var declarations = AnEnsDeclaration();
        var handler = new ApproveManifestHandler(
            repository, AConvoy(), Substitute.For<IManifestWorkQueue>(), declarations);

        await handler.HandleAsync(new ApproveManifestCommand(Id), TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            declarations.GetAsync(Id, Arg.Any<CancellationToken>());
            repository.ConfirmAndFreezeAsync(Id, ManifestStatus.Proposed, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Queues_no_envelope_for_a_manifest_it_refuses_to_approve()
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(AManifest(ManifestStatus.Created));
        var queue = Substitute.For<IManifestWorkQueue>();
        var handler = new ApproveManifestHandler(repository, AConvoy(), queue, AnEnsDeclaration());

        await handler.HandleAsync(new ApproveManifestCommand(Id), TestContext.Current.CancellationToken);

        await queue.DidNotReceive().EnqueueEloEnvelopeAsync(
            Arg.Any<EloEnvelopeRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A failed envelope hand-off leaves the manifest frozen with paperwork that will never be
    /// produced. That has to be noticed, so it is counted and logged under its own stage before it
    /// propagates — the GMR and the document already work this way.
    /// </summary>
    [Fact]
    public async Task Reports_its_own_stage_when_the_envelope_cannot_be_handed_off()
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(AManifest());
        repository.ConfirmAndFreezeAsync(Id, ManifestStatus.Proposed, Arg.Any<CancellationToken>()).Returns(Stamped);
        var queue = Substitute.For<IManifestWorkQueue>();
        queue.EnqueueEloEnvelopeAsync(Arg.Any<EloEnvelopeRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the queue is unavailable"));
        var logs = TestLoggerFactory.Create();
        var handler = new ApproveManifestHandler(
            repository, AConvoy(), queue, AnEnsDeclaration(),
            metrics: null, logger: logs.CreateLogger<ApproveManifestHandler>());

        var act = () => handler.HandleAsync(new ApproveManifestCommand(Id), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var written = string.Join(" ", logs.Sink.LogEntries.Select(entry => entry.Message));
        written.Should().Contain("elo").And.Contain(Id);
    }

    [Fact]
    public async Task Reports_not_found_for_a_manifest_that_does_not_exist()
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns((ManifestReadModel?)null);
        var handler = new ApproveManifestHandler(
            repository, AConvoy(), Substitute.For<IManifestWorkQueue>(), AnEnsDeclaration());

        var outcome = await handler.HandleAsync(new ApproveManifestCommand(Id), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.NotFound);
    }
}
