using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>
/// The manifest state machine from <c>docs/manifest-status.puml</c>, which
/// <c>docs/recommendations.md</c> §5.3 names as the authoritative design.
/// </summary>
/// <remarks>
/// Worth pinning edge by edge rather than trusting the table it guards. The status model decides
/// where GMR submission is triggered from, and a manifest that can slide backwards past
/// <c>Confirmed</c> is a manifest that can change after HMRC has been told what is in the vehicle
/// — the one thing §5.2 rules out.
/// </remarks>
public class ManifestTransitionsTests
{
    /// <summary>Every edge drawn in docs/manifest-status.puml, transcribed by hand.</summary>
    private static readonly (ManifestStatus From, ManifestStatus To)[] Diagram =
    [
        (ManifestStatus.Created, ManifestStatus.Proposed),
        (ManifestStatus.Created, ManifestStatus.Rejected),
        (ManifestStatus.Proposed, ManifestStatus.Rejected),
        (ManifestStatus.Rejected, ManifestStatus.Proposed),
        (ManifestStatus.Proposed, ManifestStatus.Confirmed),
        (ManifestStatus.Confirmed, ManifestStatus.Preparing),
        (ManifestStatus.Preparing, ManifestStatus.Ready),
        (ManifestStatus.Ready, ManifestStatus.InTransit),
        (ManifestStatus.InTransit, ManifestStatus.Delivered),
        (ManifestStatus.InTransit, ManifestStatus.Lost),
        (ManifestStatus.Delivered, ManifestStatus.Returned),
    ];

    public static TheoryData<ManifestStatus, ManifestStatus> LegalEdges
    {
        get
        {
            var data = new TheoryData<ManifestStatus, ManifestStatus>();

            foreach (var (from, to) in Diagram)
            {
                data.Add(from, to);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(LegalEdges))]
    public void Allows_every_edge_the_diagram_draws(ManifestStatus from, ManifestStatus to) =>
        ManifestTransitions.CanTransition(from, to).Should().BeTrue();

    [Fact]
    public void Allows_exactly_the_edges_the_diagram_draws_and_no_others()
    {
        var legal = Diagram.ToHashSet();

        var every = Enum.GetValues<ManifestStatus>();
        var permitted = every
            .SelectMany(_ => every, (origin, destination) => (origin, destination))
            .Where(edge => ManifestTransitions.CanTransition(edge.origin, edge.destination))
            .ToHashSet();

        permitted.Should().BeEquivalentTo(legal);
    }

    [Fact]
    public void Refuses_to_reopen_a_confirmed_manifest()
    {
        // Confirmation is what releases the manifest to GMR submission and box preparation.
        // The diagram draws no way back, and adding one would let a manifest change under HMRC.
        ManifestTransitions.CanTransition(ManifestStatus.Confirmed, ManifestStatus.Proposed).Should().BeFalse();
        ManifestTransitions.CanTransition(ManifestStatus.Preparing, ManifestStatus.Confirmed).Should().BeFalse();
    }

    [Fact]
    public void Refuses_to_lose_a_manifest_that_has_already_been_delivered()
    {
        // Only a manifest in transit can be lost. Losing a delivered one would erase a
        // confirmed delivery, which is the record the Ground Officer works from.
        ManifestTransitions.CanTransition(ManifestStatus.Delivered, ManifestStatus.Lost).Should().BeFalse();
    }

    [Fact]
    public void Refuses_to_skip_the_preparation_steps()
    {
        // Boxes are validated and loaded between Confirmed and InTransit. Skipping straight to
        // transit would put a vehicle on a ferry with cargo nobody weighed for the border check.
        ManifestTransitions.CanTransition(ManifestStatus.Confirmed, ManifestStatus.InTransit).Should().BeFalse();
        ManifestTransitions.CanTransition(ManifestStatus.Proposed, ManifestStatus.Preparing).Should().BeFalse();
    }

    [Theory]
    [InlineData(ManifestStatus.Lost)]
    [InlineData(ManifestStatus.Returned)]
    public void Treats_lost_and_returned_as_terminal(ManifestStatus terminal) =>
        Enum.GetValues<ManifestStatus>()
            .Any(to => ManifestTransitions.CanTransition(terminal, to))
            .Should().BeFalse();

    [Fact]
    public void Refuses_a_transition_to_the_state_a_manifest_is_already_in() =>
        Enum.GetValues<ManifestStatus>()
            .Any(status => ManifestTransitions.CanTransition(status, status))
            .Should().BeFalse();
}
