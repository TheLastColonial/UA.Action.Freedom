using AwesomeAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Declarations;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Declarations;

/// <summary>
/// Recording, refusing and filing a vehicle's declarations (ADR 0005, ADR 0006): manual by default,
/// automatic only where an authority's mode says so, ELO only after an accepted ENS.
/// </summary>
public class DeclarationHandlerTests
{
    private const string Vin = "WVWZZZ1JZXW000001";

    private const string ManifestId = "MAN-0001";

    private const string Mrn = "25FR17551780961AT5";

    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    private static DeclarationReadModel ADeclaration(
        DeclarationKind kind, DeclarationStatus status, string? reference = null) => new(
        9, 42, Vin, kind, status, null, reference, null, null, null, null, null);

    private static IDeclarationRepository ARepositoryWithAnAcceptedEns()
    {
        var repository = Substitute.For<IDeclarationRepository>();
        repository.GetCurrentAsync(42, Vin, DeclarationKind.Ens, null, Arg.Any<CancellationToken>())
            .Returns(ADeclaration(DeclarationKind.Ens, DeclarationStatus.Accepted, Mrn));
        repository.RecordReferenceAsync(
                Arg.Any<int>(), Arg.Any<string>(), Arg.Any<DeclarationKind>(), Arg.Any<Guid?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(RecordReferenceResult.Recorded);
        return repository;
    }

    private static IManifestRepository AnApprovedManifest()
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetForVehicleAsync(42, Vin, Arg.Any<CancellationToken>()).Returns(
            new ManifestReadModel(ManifestId, 42, Vin, ManifestStatus.Confirmed, null, GmrSubmittedAt: Departs));
        repository.GetVehiclePlateAsync(ManifestId, Arg.Any<CancellationToken>()).Returns("AB12 CDE");
        return repository;
    }

    private static IConvoyRepository AConvoy()
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(42, Arg.Any<CancellationToken>()).Returns(
            new ConvoyReadModel(42, Departs, Departs.AddDays(4), Departs.AddDays(-10)));
        return convoys;
    }

    private static FileDeclarationHandler AFiler(
        IDeclarationRepository declarations,
        IManifestWorkQueue queue,
        IManifestRepository? manifests = null,
        SubmissionMode gmr = SubmissionMode.Automatic,
        SubmissionMode elo = SubmissionMode.Automatic) =>
        new(declarations, manifests ?? AnApprovedManifest(), AConvoy(), queue, new DeclarationSubmissionModes(gmr, elo));

    // --- record -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(DeclarationKind.Gmr)]
    [InlineData(DeclarationKind.Elo)]
    public async Task Records_a_reference_the_dispatcher_obtained_in_the_portal(DeclarationKind kind)
    {
        var repository = ARepositoryWithAnAcceptedEns();

        var outcome = await new RecordDeclarationHandler(repository).HandleAsync(
            new RecordDeclarationCommand(42, Vin, kind, "REF-1"), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordDeclarationOutcome.Recorded);
        await repository.Received(1).RecordReferenceAsync(
            42, Vin, kind, null, "REF-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_goods_list_is_recorded_per_receiver()
    {
        var receiver = Guid.NewGuid();
        var repository = ARepositoryWithAnAcceptedEns();

        var outcome = await new RecordDeclarationHandler(repository).HandleAsync(
            new RecordDeclarationCommand(42, Vin, DeclarationKind.GoodsList, "UA-77", receiver),
            TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordDeclarationOutcome.Recorded);
        await repository.Received(1).RecordReferenceAsync(
            42, Vin, DeclarationKind.GoodsList, receiver, "UA-77", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_goods_list_needs_its_receiver_and_no_other_kind_takes_one()
    {
        var handler = new RecordDeclarationHandler(ARepositoryWithAnAcceptedEns());

        (await handler.HandleAsync(
                new RecordDeclarationCommand(42, Vin, DeclarationKind.GoodsList, "UA-77"),
                TestContext.Current.CancellationToken))
            .Should().Be(RecordDeclarationOutcome.ReceiverRequired);
        (await handler.HandleAsync(
                new RecordDeclarationCommand(42, Vin, DeclarationKind.Gmr, "G-1", Guid.NewGuid()),
                TestContext.Current.CancellationToken))
            .Should().Be(RecordDeclarationOutcome.ReceiverNotAllowed);
    }

    [Fact]
    public async Task The_ens_is_recorded_on_its_own_route_because_it_carries_more_than_a_reference()
    {
        var outcome = await new RecordDeclarationHandler(ARepositoryWithAnAcceptedEns()).HandleAsync(
            new RecordDeclarationCommand(42, Vin, DeclarationKind.Ens, Mrn), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordDeclarationOutcome.UseEnsRoute);
    }

    /// <summary>The ELO is created from the ENS MRN, so ENS comes first (ADR 0005).</summary>
    [Fact]
    public async Task Refuses_to_record_an_elo_before_an_ens_is_accepted()
    {
        var repository = Substitute.For<IDeclarationRepository>();

        var outcome = await new RecordDeclarationHandler(repository).HandleAsync(
            new RecordDeclarationCommand(42, Vin, DeclarationKind.Elo, "ELO-1"), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordDeclarationOutcome.EnsNotAccepted);
        await repository.DidNotReceive().RecordReferenceAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<DeclarationKind>(), Arg.Any<Guid?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_not_overwrite_a_reference_that_is_already_recorded()
    {
        var repository = ARepositoryWithAnAcceptedEns();
        repository.RecordReferenceAsync(
                42, Vin, DeclarationKind.Gmr, null, "G-2", Arg.Any<CancellationToken>())
            .Returns(RecordReferenceResult.AlreadyRecorded);

        var outcome = await new RecordDeclarationHandler(repository).HandleAsync(
            new RecordDeclarationCommand(42, Vin, DeclarationKind.Gmr, "G-2"), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordDeclarationOutcome.AlreadyRecorded);
    }

    [Fact]
    public async Task Reports_a_vehicle_that_is_not_on_the_convoy()
    {
        var repository = ARepositoryWithAnAcceptedEns();
        repository.RecordReferenceAsync(
                42, Vin, DeclarationKind.Gmr, null, "G-1", Arg.Any<CancellationToken>())
            .Returns(RecordReferenceResult.VehicleNotOnConvoy);

        var outcome = await new RecordDeclarationHandler(repository).HandleAsync(
            new RecordDeclarationCommand(42, Vin, DeclarationKind.Gmr, "G-1"), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordDeclarationOutcome.VehicleNotOnConvoy);
    }

    // --- refuse -----------------------------------------------------------------------------------

    [Fact]
    public async Task Records_a_refusal_with_a_bounded_reason_code()
    {
        var repository = Substitute.For<IDeclarationRepository>();
        repository.GetCurrentAsync(42, Vin, DeclarationKind.Gmr, null, Arg.Any<CancellationToken>())
            .Returns(ADeclaration(DeclarationKind.Gmr, DeclarationStatus.Filed, "G-1"));
        repository.RefuseAsync(9, "data-error", Arg.Any<CancellationToken>()).Returns(true);

        var outcome = await new RefuseDeclarationHandler(repository).HandleAsync(
            new RefuseDeclarationCommand(42, Vin, DeclarationKind.Gmr, "data-error"),
            TestContext.Current.CancellationToken);

        outcome.Should().Be(RefuseDeclarationOutcome.Refused);
    }

    /// <summary>The authority's own text can quote the declaration it objected to, so it is never stored.</summary>
    [Fact]
    public async Task Refuses_a_reason_that_is_not_one_of_the_known_codes()
    {
        var repository = Substitute.For<IDeclarationRepository>();

        var outcome = await new RefuseDeclarationHandler(repository).HandleAsync(
            new RefuseDeclarationCommand(42, Vin, DeclarationKind.Gmr, "The consignee at 12 Vulytsia Sumska is unknown"),
            TestContext.Current.CancellationToken);

        outcome.Should().Be(RefuseDeclarationOutcome.UnknownReason);
        await repository.DidNotReceive().RefuseAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(DeclarationStatus.Draft)]
    [InlineData(DeclarationStatus.Accepted)]
    [InlineData(DeclarationStatus.Closed)]
    public async Task Only_a_filed_declaration_can_be_refused(DeclarationStatus status)
    {
        var repository = Substitute.For<IDeclarationRepository>();
        repository.GetCurrentAsync(42, Vin, DeclarationKind.Gmr, null, Arg.Any<CancellationToken>())
            .Returns(ADeclaration(DeclarationKind.Gmr, status));

        var outcome = await new RefuseDeclarationHandler(repository).HandleAsync(
            new RefuseDeclarationCommand(42, Vin, DeclarationKind.Gmr, "technical"),
            TestContext.Current.CancellationToken);

        outcome.Should().Be(RefuseDeclarationOutcome.NotFiled);
    }

    [Fact]
    public async Task Reports_a_refusal_of_a_declaration_that_was_never_recorded()
    {
        var outcome = await new RefuseDeclarationHandler(Substitute.For<IDeclarationRepository>()).HandleAsync(
            new RefuseDeclarationCommand(42, Vin, DeclarationKind.Gmr, "technical"),
            TestContext.Current.CancellationToken);

        outcome.Should().Be(RefuseDeclarationOutcome.NotFound);
    }

    // --- file -------------------------------------------------------------------------------------

    /// <summary>ADR 0006: manual is the default, and the system must not assume an authority's API is live.</summary>
    [Fact]
    public async Task In_manual_mode_filing_enqueues_nothing_and_says_to_record_the_reference()
    {
        var queue = Substitute.For<IManifestWorkQueue>();
        var handler = AFiler(ARepositoryWithAnAcceptedEns(), queue, gmr: SubmissionMode.Manual);

        var outcome = await handler.HandleAsync(
            new FileDeclarationCommand(42, Vin, DeclarationKind.Gmr), TestContext.Current.CancellationToken);

        outcome.Should().Be(FileDeclarationOutcome.ManualMode);
        await queue.DidNotReceive().EnqueueGmrSubmissionAsync(
            Arg.Any<GmrSubmissionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_default_modes_are_manual()
    {
        new DeclarationSubmissionModes().For(DeclarationKind.Gmr).Should().Be(SubmissionMode.Manual);
        new DeclarationSubmissionModes().For(DeclarationKind.Elo).Should().Be(SubmissionMode.Manual);
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData(DeclarationKind.Ens)]
    [InlineData(DeclarationKind.GoodsList)]
    public async Task The_ens_and_the_goods_list_can_never_be_filed_automatically(DeclarationKind kind)
    {
        var outcome = await AFiler(ARepositoryWithAnAcceptedEns(), Substitute.For<IManifestWorkQueue>())
            .HandleAsync(new FileDeclarationCommand(42, Vin, kind), TestContext.Current.CancellationToken);

        outcome.Should().Be(FileDeclarationOutcome.NotAutomatable);
    }

    [Fact]
    public async Task In_automatic_mode_filing_the_gmr_enqueues_it_with_the_plate_and_marks_it_filed()
    {
        var repository = ARepositoryWithAnAcceptedEns();
        var queue = Substitute.For<IManifestWorkQueue>();

        var outcome = await AFiler(repository, queue).HandleAsync(
            new FileDeclarationCommand(42, Vin, DeclarationKind.Gmr), TestContext.Current.CancellationToken);

        outcome.Should().Be(FileDeclarationOutcome.Filed);
        await queue.Received(1).EnqueueGmrSubmissionAsync(
            Arg.Is<GmrSubmissionRequest>(request =>
                request.ManifestId == ManifestId && request.VehicleRegistration == "AB12 CDE"
                && request.DepartsAt == Departs),
            Arg.Any<CancellationToken>());
        await repository.Received(1).RecordReferenceAsync(
            42, Vin, DeclarationKind.Gmr, null, null, Arg.Any<CancellationToken>());
    }

    /// <summary>The message goes first and the stamp second: a failed enqueue must not leave a "filed" declaration.</summary>
    [Fact]
    public async Task Enqueues_before_it_marks_the_declaration_filed()
    {
        var repository = ARepositoryWithAnAcceptedEns();
        var queue = Substitute.For<IManifestWorkQueue>();

        await AFiler(repository, queue).HandleAsync(
            new FileDeclarationCommand(42, Vin, DeclarationKind.Gmr), TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            queue.EnqueueGmrSubmissionAsync(Arg.Any<GmrSubmissionRequest>(), Arg.Any<CancellationToken>());
            repository.RecordReferenceAsync(
                42, Vin, DeclarationKind.Gmr, null, null, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task A_failed_enqueue_leaves_the_declaration_unfiled()
    {
        var repository = ARepositoryWithAnAcceptedEns();
        var queue = Substitute.For<IManifestWorkQueue>();
        queue.EnqueueGmrSubmissionAsync(Arg.Any<GmrSubmissionRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the queue is unavailable"));

        var act = () => AFiler(repository, queue).HandleAsync(
            new FileDeclarationCommand(42, Vin, DeclarationKind.Gmr), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await repository.DidNotReceive().RecordReferenceAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<DeclarationKind>(), Arg.Any<Guid?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The point of the whole ICS2 work: the envelope names the MRN the crossing was accepted under.</summary>
    [Fact]
    public async Task Filing_the_elo_hands_the_accepted_ens_mrn_to_the_envelope()
    {
        var queue = Substitute.For<IManifestWorkQueue>();

        var outcome = await AFiler(ARepositoryWithAnAcceptedEns(), queue).HandleAsync(
            new FileDeclarationCommand(42, Vin, DeclarationKind.Elo), TestContext.Current.CancellationToken);

        outcome.Should().Be(FileDeclarationOutcome.Filed);
        await queue.Received(1).EnqueueEloEnvelopeAsync(
            Arg.Is<EloEnvelopeRequest>(request =>
                request.ManifestId == ManifestId
                && request.Profile == EloCrossingProfile.HumanitarianAidToUkraine
                && request.DeclarationIdentifiers.Count == 1 && request.DeclarationIdentifiers[0] == Mrn),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_to_file_an_elo_without_an_accepted_ens()
    {
        var repository = Substitute.For<IDeclarationRepository>();
        var queue = Substitute.For<IManifestWorkQueue>();

        var outcome = await AFiler(repository, queue).HandleAsync(
            new FileDeclarationCommand(42, Vin, DeclarationKind.Elo), TestContext.Current.CancellationToken);

        outcome.Should().Be(FileDeclarationOutcome.EnsNotAccepted);
        await queue.DidNotReceive().EnqueueEloEnvelopeAsync(
            Arg.Any<EloEnvelopeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_to_file_before_the_load_is_signed_off()
    {
        var manifests = Substitute.For<IManifestRepository>();
        manifests.GetForVehicleAsync(42, Vin, Arg.Any<CancellationToken>()).Returns(
            new ManifestReadModel(ManifestId, 42, Vin, ManifestStatus.Proposed, null, GmrSubmittedAt: null));
        var queue = Substitute.For<IManifestWorkQueue>();

        var outcome = await AFiler(ARepositoryWithAnAcceptedEns(), queue, manifests).HandleAsync(
            new FileDeclarationCommand(42, Vin, DeclarationKind.Gmr), TestContext.Current.CancellationToken);

        outcome.Should().Be(FileDeclarationOutcome.ManifestNotApproved);
        await queue.DidNotReceive().EnqueueGmrSubmissionAsync(
            Arg.Any<GmrSubmissionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_not_enqueue_a_second_submission_for_a_declaration_already_filed()
    {
        var repository = ARepositoryWithAnAcceptedEns();
        repository.GetCurrentAsync(42, Vin, DeclarationKind.Gmr, null, Arg.Any<CancellationToken>())
            .Returns(ADeclaration(DeclarationKind.Gmr, DeclarationStatus.Filed));
        var queue = Substitute.For<IManifestWorkQueue>();

        var outcome = await AFiler(repository, queue).HandleAsync(
            new FileDeclarationCommand(42, Vin, DeclarationKind.Gmr), TestContext.Current.CancellationToken);

        outcome.Should().Be(FileDeclarationOutcome.AlreadyFiled);
        await queue.DidNotReceive().EnqueueGmrSubmissionAsync(
            Arg.Any<GmrSubmissionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_a_vehicle_with_no_manifest()
    {
        var manifests = Substitute.For<IManifestRepository>();

        var outcome = await AFiler(ARepositoryWithAnAcceptedEns(), Substitute.For<IManifestWorkQueue>(), manifests)
            .HandleAsync(new FileDeclarationCommand(42, Vin, DeclarationKind.Gmr), TestContext.Current.CancellationToken);

        outcome.Should().Be(FileDeclarationOutcome.VehicleNotOnConvoy);
    }

    // --- listing ----------------------------------------------------------------------------------

    [Fact]
    public async Task Lists_nothing_for_a_vehicle_that_is_not_on_the_convoy()
    {
        var truckList = Substitute.For<IConvoyVehicleRepository>();

        var read = await new ListDeclarationsHandler(truckList, Substitute.For<IDeclarationRepository>())
            .HandleAsync(new ListDeclarationsQuery(42, Vin), TestContext.Current.CancellationToken);

        read.Should().BeNull();
    }
}
