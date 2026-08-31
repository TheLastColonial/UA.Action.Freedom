using AwesomeAssertions;
using NSubstitute;
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

    private static ManifestReadModel AManifest(
        ManifestStatus status = ManifestStatus.Proposed, bool frozen = false) => new(
        Id, "WVWZZZ1JZXW000001", 42, status, null, FerryBookingComplete: false,
        GmrSubmittedAt: frozen ? Stamped : null);

    [Fact]
    public async Task Confirms_the_manifest_and_queues_its_goods_movement_record()
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(AManifest());
        repository.ConfirmAndFreezeAsync(Id, ManifestStatus.Proposed, Arg.Any<CancellationToken>()).Returns(Stamped);
        var queue = Substitute.For<IManifestWorkQueue>();
        var handler = new ApproveManifestHandler(repository, AConvoy(), queue);

        var outcome = await handler.HandleAsync(new ApproveManifestCommand(Id), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.Transitioned);
        await queue.Received(1).EnqueueGmrSubmissionAsync(
            Arg.Is<GmrSubmissionRequest>(request =>
                request.ManifestId == Id
                && request.VehicleRegistration == "WVWZZZ1JZXW000001"
                && request.DepartsAt == Departs),
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
        var handler = new ApproveManifestHandler(repository, AConvoy(), queue);

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
        var handler = new ApproveManifestHandler(repository, AConvoy(), queue);

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
        var handler = new ApproveManifestHandler(repository, AConvoy(), queue);

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
        var handler = new ApproveManifestHandler(repository, AConvoy(), queue);

        var outcome = await handler.HandleAsync(new ApproveManifestCommand(Id), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.IllegalTransition);
        await queue.DidNotReceive().EnqueueGmrSubmissionAsync(
            Arg.Any<GmrSubmissionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_not_found_for_a_manifest_that_does_not_exist()
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns((ManifestReadModel?)null);
        var handler = new ApproveManifestHandler(repository, AConvoy(), Substitute.For<IManifestWorkQueue>());

        var outcome = await handler.HandleAsync(new ApproveManifestCommand(Id), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.NotFound);
    }
}
