using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Manifests;

/// <summary>
/// Approval signs off the load and nothing more (ADR 0004, ADR 0006): it confirms the manifest and
/// freezes it, and files, enqueues and hands off nothing. The GMR and the French envelope are filed
/// from the vehicle's declarations afterwards, and the travelling document is requested explicitly.
/// </summary>
public class ApproveManifestHandlerTests
{
    private const string Id = "MAN-0001";

    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly DateTime Stamped = new(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

    private static ManifestReadModel AManifest(
        ManifestStatus status = ManifestStatus.Proposed, bool frozen = false) => new(
        Id, 42, Vin, status, null,
        GmrSubmittedAt: frozen ? Stamped : null);

    private static IManifestRepository ARepositoryHolding(ManifestReadModel? manifest)
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(manifest);
        repository.ConfirmAndFreezeAsync(Id, ManifestStatus.Proposed, Arg.Any<CancellationToken>()).Returns(Stamped);
        return repository;
    }

    [Fact]
    public async Task Confirms_and_freezes_the_manifest()
    {
        var repository = ARepositoryHolding(AManifest());

        var outcome = await new ApproveManifestHandler(repository).HandleAsync(
            new ApproveManifestCommand(Id), TestContext.Current.CancellationToken);

        outcome.Should().Be(TransitionManifestOutcome.Transitioned);
        await repository.Received(1).ConfirmAndFreezeAsync(
            Id, ManifestStatus.Proposed, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// No ENS is needed to sign a load off. The envelope that needed one is no longer a side effect of
    /// approval, so a manifest is approvable the moment it is proposed.
    /// </summary>
    [Fact]
    public async Task Does_not_ask_for_an_entry_summary_declaration()
    {
        var repository = ARepositoryHolding(AManifest());

        var outcome = await new ApproveManifestHandler(repository).HandleAsync(
            new ApproveManifestCommand(Id), TestContext.Current.CancellationToken);

        outcome.Should().Be(TransitionManifestOutcome.Transitioned);
    }

    [Fact]
    public async Task Refuses_to_approve_a_manifest_that_was_never_proposed()
    {
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Created));

        var outcome = await new ApproveManifestHandler(repository).HandleAsync(
            new ApproveManifestCommand(Id), TestContext.Current.CancellationToken);

        outcome.Should().Be(TransitionManifestOutcome.IllegalTransition);
        await repository.DidNotReceive().ConfirmAndFreezeAsync(
            Arg.Any<string>(), Arg.Any<ManifestStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_to_approve_a_manifest_twice()
    {
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Confirmed, frozen: true));

        var outcome = await new ApproveManifestHandler(repository).HandleAsync(
            new ApproveManifestCommand(Id), TestContext.Current.CancellationToken);

        outcome.Should().Be(TransitionManifestOutcome.Frozen);
    }

    [Fact]
    public async Task Reports_a_manifest_that_moved_underneath_us_as_an_illegal_transition()
    {
        // The conditional confirm found nothing, so somebody else already approved it.
        var repository = ARepositoryHolding(AManifest());
        repository.ConfirmAndFreezeAsync(Id, ManifestStatus.Proposed, Arg.Any<CancellationToken>())
            .Returns((DateTime?)null);

        var outcome = await new ApproveManifestHandler(repository).HandleAsync(
            new ApproveManifestCommand(Id), TestContext.Current.CancellationToken);

        outcome.Should().Be(TransitionManifestOutcome.IllegalTransition);
    }

    [Fact]
    public async Task Reports_not_found_for_a_manifest_that_does_not_exist()
    {
        var outcome = await new ApproveManifestHandler(ARepositoryHolding(null)).HandleAsync(
            new ApproveManifestCommand(Id), TestContext.Current.CancellationToken);

        outcome.Should().Be(TransitionManifestOutcome.NotFound);
    }

    /// <summary>
    /// A request carries a crossing, not a load. There is nowhere on it to put a receiver, an address or
    /// a box, and this pins that rather than trusting it: the request is serialised onto a durable queue.
    /// </summary>
    [Fact]
    public void The_envelope_request_has_nowhere_to_put_a_delivery_detail()
    {
        typeof(EloEnvelopeRequest).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo("ManifestId", "Profile", "DeclarationIdentifiers");
    }

    /// <summary>
    /// The ENS MRN does not reach HMRC, and that is a finding rather than an omission: GVMS wants it in
    /// <c>sAndSMasterRefNum</c>, which hangs off a declaration container whose primary identifier Freedom
    /// does not hold (docs/gotchas-and-open-questions.md 5b).
    /// </summary>
    [Fact]
    public void The_goods_movement_record_has_nowhere_to_put_the_declaration()
    {
        typeof(GmrSubmissionRequest).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo("ManifestId", "VehicleRegistration", "DepartsAt");
    }

    [Fact]
    public async Task The_travelling_document_is_requested_separately_and_carries_the_plate_not_the_vin()
    {
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Confirmed, frozen: true));
        repository.GetVehiclePlateAsync(Id, Arg.Any<CancellationToken>()).Returns("AB12 CDE");
        repository.GetDocumentLinesAsync(Id, Arg.Any<CancellationToken>()).Returns([]);
        var queue = Substitute.For<IManifestWorkQueue>();

        var outcome = await new RequestManifestDocumentHandler(repository, queue).HandleAsync(
            new RequestManifestDocumentCommand(Id), TestContext.Current.CancellationToken);

        outcome.Should().Be(RequestManifestDocumentOutcome.Requested);
        await queue.Received(1).EnqueueDocumentAsync(
            Arg.Is<ManifestDocumentRequest>(document => document.VehicleRegistration == "AB12 CDE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_travelling_document_is_refused_before_the_load_is_signed_off()
    {
        var queue = Substitute.For<IManifestWorkQueue>();

        var outcome = await new RequestManifestDocumentHandler(ARepositoryHolding(AManifest()), queue).HandleAsync(
            new RequestManifestDocumentCommand(Id), TestContext.Current.CancellationToken);

        outcome.Should().Be(RequestManifestDocumentOutcome.NotApproved);
        await queue.DidNotReceive().EnqueueDocumentAsync(
            Arg.Any<ManifestDocumentRequest>(), Arg.Any<CancellationToken>());
    }
}
