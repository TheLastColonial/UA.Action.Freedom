using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Manifests;

/// <summary>
/// Proposing and rejecting a manifest, and refusing to send an approved one back by hand.
/// </summary>
/// <remarks>
/// The legality of a move is decided by <see cref="ManifestTransitions"/>, which is pinned edge-by-edge elsewhere.
/// What is tested here is what the diagram cannot say: a manifest may only be proposed against a convoy whose truck
/// list is published (docs/process.puml), and a person cannot send an approved manifest back, because only a change
/// to the load does (X3).
/// </remarks>
public class ManifestTransitionHandlerTests
{
    private const string Id = "MAN-0001";
    private const int ConvoyId = 42;

    private static ManifestReadModel AManifest(
        ManifestStatus status = ManifestStatus.Proposed,
        int convoyId = ConvoyId) => new(
        Id,
        ConvoyId: convoyId,
        Vin: Vin,
        Status: status,
        DeliveryNotes: null);

    private const string Vin = "WVWZZZ1JZXW000001";

    /// <summary>
    /// The handler reads the convoy for its truck-list stamp and the truck-list entry for the
    /// vehicle's insurance. One substitute stands in for both ports, because in these tests they
    /// describe the same convoy and splitting them would only add ceremony.
    /// </summary>
    private static TransitionManifestHandler AHandler(IManifestRepository repository, IConvoyRepository convoys) =>
        new(repository, convoys);

    /// <summary>A convoy with its truck list published — the state every test here assumes unless it is about that.</summary>
    private static IConvoyRepository AConvoyWithPublishedTruckList(bool published = true) =>
        AConvoy(published);

    private static IConvoyRepository AConvoy(bool published = true)
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
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Rejected));
        var handler = AHandler(repository, AConvoyWithPublishedTruckList());

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Proposed), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.Transitioned);
        await repository.Received(1).TransitionAsync(
            Id, ManifestStatus.Rejected, ManifestStatus.Proposed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_to_propose_against_a_convoy_whose_truck_list_is_still_open()
    {
        // The manifest is proposed against a fixed set of vehicles. Propose before the list is
        // published and the truck it names could still leave the convoy.
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Rejected));
        var handler = AHandler(repository, AConvoyWithPublishedTruckList(published: false));

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Proposed), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.TruckListNotPublished);
        await repository.DidNotReceive().TransitionAsync(
            Arg.Any<string>(), Arg.Any<ManifestStatus>(), Arg.Any<ManifestStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_to_propose_against_a_convoy_that_does_not_exist()
    {
        // A manifest naming no convoy at all used to be expressible, and was refused here. It no
        // longer is: ConvoyId and Vin are non-nullable and are a composite foreign key to the
        // truck-list entry, so the case this once guarded cannot be constructed. What remains
        // reachable is a convoy the read cannot find, and that is refused the same way.
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Rejected));
        var convoys = Substitute.For<IConvoyRepository, IConvoyVehicleRepository>();
        convoys.GetByIdAsync(ConvoyId, Arg.Any<CancellationToken>()).Returns((ConvoyReadModel?)null);

        var outcome = await AHandler(repository, convoys).HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Proposed), TestContext.Current.CancellationToken);

        outcome.Should().Be(TransitionManifestOutcome.TruckListNotPublished);
    }

    [Fact]
    public async Task Refuses_an_edge_the_diagram_does_not_draw()
    {
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Rejected));
        var handler = AHandler(repository, AConvoyWithPublishedTruckList());

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Approved), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.IllegalTransition);
    }

    [Fact]
    public async Task Reports_not_found_for_a_manifest_that_does_not_exist()
    {
        var repository = ARepositoryHolding(null);
        var handler = AHandler(repository, AConvoyWithPublishedTruckList());

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Proposed), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.NotFound);
    }

    [Fact]
    public async Task Reports_an_illegal_transition_when_the_manifest_moved_underneath_us()
    {
        // The conditional UPDATE found nothing, so somebody else transitioned it first. Two
        // dispatchers pressing the same button must resolve to one transition.
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Proposed), transitions: false);
        var handler = AHandler(repository, AConvoyWithPublishedTruckList());

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Rejected), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.IllegalTransition);
    }

    [Fact]
    public async Task A_person_cannot_send_an_approved_manifest_back()
    {
        // Only a change to the load does that, in the same transaction as the change (X3). Asking for it by
        // hand would reopen a sign-off with the load exactly as the Administrator saw it.
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Approved));
        var handler = AHandler(repository, AConvoyWithPublishedTruckList());

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Proposed), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.IllegalTransition);
        await repository.DidNotReceive().TransitionAsync(
            Arg.Any<string>(), Arg.Any<ManifestStatus>(), Arg.Any<ManifestStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_approved_manifest_cannot_be_rejected()
    {
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Approved));

        var outcome = await AHandler(repository, AConvoyWithPublishedTruckList()).HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Rejected), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.IllegalTransition);
    }

    [Fact]
    public async Task A_rejected_manifest_can_be_proposed_again()
    {
        // The one backward edge the diagram draws: rejection is recoverable.
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Rejected));
        var handler = AHandler(repository, AConvoyWithPublishedTruckList());

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Proposed), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.Transitioned);
    }

    [Fact]
    public async Task Rejecting_a_manifest_needs_no_published_truck_list()
    {
        // Only proposal is gated on the truck list. Rejection must always be available.
        var repository = ARepositoryHolding(AManifest(ManifestStatus.Proposed));
        var handler = AHandler(repository, AConvoyWithPublishedTruckList(published: false));

        var outcome = await handler.HandleAsync(
            new TransitionManifestCommand(Id, ManifestStatus.Rejected), CancellationToken.None);

        outcome.Should().Be(TransitionManifestOutcome.Transitioned);
    }
}
