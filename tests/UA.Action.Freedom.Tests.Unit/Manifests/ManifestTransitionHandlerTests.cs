using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Manifests;

/// <summary>
/// Moving a manifest through its lifecycle, and the three rules that govern it.
/// </summary>
/// <remarks>
/// The legality of a move is decided by <see cref="ManifestTransitions"/>, which is pinned
/// edge-by-edge elsewhere. What is tested here is what the diagram cannot say: a manifest may
/// only be proposed against a convoy whose truck list is published (docs/process.puml), and once
/// its GMR exists it may only record what happened to the load (docs/recommendations.md §5.2).
/// </remarks>
public class ManifestTransitionHandlerTests
{
    private const string Id = "MAN-0001";
    private const int ConvoyId = 42;

    private static ManifestReadModel AManifest(
        ManifestStatus status = ManifestStatus.Created,
        bool frozen = false,
        int? convoyId = ConvoyId) => new(
        Id,
        Vin: "WVWZZZ1JZXW000001",
        ConvoyId: convoyId,
        Status: status,
        DeliveryNotes: null,
        FerryBookingComplete: false,
        GmrSubmittedAt: frozen ? new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc) : null);

    private static IConvoyRepository AConvoyWithPublishedTruckList(bool published = true)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyId, Arg.Any<CancellationToken>()).Returns(
            new ConvoyReadModel(
                ConvoyId,
                new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 5, 18, 0, 0, DateTimeKind.Utc),
                published ? new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc) : null));
        return convoys;
    }

    private static IManifestRepository ARepositoryHolding(ManifestReadModel? manifest, bool transitions = true)
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(manifest);
        repository.TransitionAsync(Id, Arg.Any<ManifestStatus>(), Arg.Any<ManifestStatus>(), Arg.Any<CancellationToken>())
            .Returns(transitions);
        return repository;
    }

    [Fact]
    public async Task Proposes_a_manifest_whose_convoy_has_published_its_truck_list()
    {
        var repository = ARepositoryHolding(AManifest());
        var handler = new TransitionManifestHandler(repository, AConvoyWithPublishedTruckList());

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Proposed), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.Transitioned);
        await repository.Received(1).TransitionAsync(
            Id, ManifestStatus.Created, ManifestStatus.Proposed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_to_propose_against_a_convoy_whose_truck_list_is_still_open()
    {
        // The manifest is proposed against a fixed set of vehicles. Propose before the list is
        // published and the truck it names could still leave the convoy.
        var repository = ARepositoryHolding(AManifest());
        var handler = new TransitionManifestHandler(repository, AConvoyWithPublishedTruckList(published: false));

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Proposed), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.TruckListNotPublished);
        await repository.DidNotReceive().TransitionAsync(
            Arg.Any<string>(), Arg.Any<ManifestStatus>(), Arg.Any<ManifestStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_to_propose_a_manifest_that_is_on_no_convoy_at_all()
    {
        var repository = ARepositoryHolding(AManifest(convoyId: null));
        var handler = new TransitionManifestHandler(repository, AConvoyWithPublishedTruckList());

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Proposed), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.TruckListNotPublished);
    }

    [Fact]
    public async Task Refuses_an_edge_the_diagram_does_not_draw()
    {
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Confirmed));
        var handler = new TransitionManifestHandler(repository, AConvoyWithPublishedTruckList());

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.InTransit), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.IllegalTransition);
    }

    [Fact]
    public async Task Reports_not_found_for_a_manifest_that_does_not_exist()
    {
        var repository = ARepositoryHolding(null);
        var handler = new TransitionManifestHandler(repository, AConvoyWithPublishedTruckList());

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Proposed), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.NotFound);
    }

    [Fact]
    public async Task Reports_an_illegal_transition_when_the_manifest_moved_underneath_us()
    {
        // The conditional UPDATE found nothing, so somebody else transitioned it first. Two
        // dispatchers pressing the same button must resolve to one transition.
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Preparing), transitions: false);
        var handler = new TransitionManifestHandler(repository, AConvoyWithPublishedTruckList());

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Ready), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.IllegalTransition);
    }

    [Theory]
    [InlineData(ManifestStatus.InTransit, ManifestStatus.Delivered)]
    [InlineData(ManifestStatus.InTransit, ManifestStatus.Lost)]
    [InlineData(ManifestStatus.Delivered, ManifestStatus.Returned)]
    public async Task A_frozen_manifest_can_still_record_what_happened_to_the_load(
        ManifestStatus from, ManifestStatus to)
    {
        // These say what the world did to the vehicle. None of them contradicts the GMR.
        var repository = ARepositoryHolding(AManifest(from, frozen: true));
        var handler = new TransitionManifestHandler(repository, AConvoyWithPublishedTruckList());

        var result = await handler.HandleAsync(new TransitionManifestCommand(Id, to), CancellationToken.None);

        result.Should().Be(TransitionManifestOutcome.Transitioned);
    }

    [Theory]
    [InlineData(ManifestStatus.Confirmed, ManifestStatus.Preparing)]
    [InlineData(ManifestStatus.Preparing, ManifestStatus.Ready)]
    [InlineData(ManifestStatus.Ready, ManifestStatus.InTransit)]
    public async Task A_frozen_manifest_still_makes_progress(ManifestStatus from, ManifestStatus to)
    {
        // §5.2 forbids edits, not progress. Blocking these would strand every approved manifest
        // in Confirmed for ever — it could never be prepared, loaded or delivered.
        var repository = ARepositoryHolding(AManifest(from, frozen: true));
        var handler = new TransitionManifestHandler(repository, AConvoyWithPublishedTruckList());

        var outcome = await handler.HandleAsync(new TransitionManifestCommand(Id, to), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.Transitioned);
    }

    [Theory]
    [InlineData(ManifestStatus.Proposed)]
    [InlineData(ManifestStatus.Rejected)]
    public async Task A_frozen_manifest_cannot_be_put_back_in_front_of_an_approver(ManifestStatus target)
    {
        // Reopening a manifest whose GMR exists would present it as something still editable.
        // The diagram makes this unreachable from Confirmed today; the guard is for the day
        // somebody adds an edge.
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Rejected, frozen: true));
        var handler = new TransitionManifestHandler(repository, AConvoyWithPublishedTruckList());

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, target), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.Frozen);
        await repository.DidNotReceive().TransitionAsync(
            Arg.Any<string>(), Arg.Any<ManifestStatus>(), Arg.Any<ManifestStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_rejected_manifest_can_be_proposed_again()
    {
        // The one backward edge the diagram draws: rejection is recoverable.
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Rejected));
        var handler = new TransitionManifestHandler(repository, AConvoyWithPublishedTruckList());

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Proposed), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.Transitioned);
    }

    [Fact]
    public async Task Rejecting_a_manifest_needs_no_published_truck_list()
    {
        // Only proposal is gated on the truck list. Rejection must always be available.
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Proposed));
        var handler = new TransitionManifestHandler(repository, AConvoyWithPublishedTruckList(published: false));

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Rejected), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.Transitioned);
    }
}
