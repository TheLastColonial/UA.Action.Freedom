using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>
/// The load sign-off from <c>docs/manifest-status.puml</c>: Proposed, Approved or Rejected (ADR 0004).
/// </summary>
/// <remarks>
/// Worth pinning edge by edge rather than trusting the table it guards. The one edge that goes backwards,
/// Approved to Proposed, is how a change to the load sends the sign-off back for re-approval (X3), and it must
/// never be one a person can ask for.
/// </remarks>
public class ManifestTransitionsTests
{
    /// <summary>Every edge drawn in docs/manifest-status.puml, transcribed by hand.</summary>
    private static readonly (ManifestStatus From, ManifestStatus To)[] Diagram =
    [
        (ManifestStatus.Proposed, ManifestStatus.Approved),
        (ManifestStatus.Proposed, ManifestStatus.Rejected),
        (ManifestStatus.Rejected, ManifestStatus.Proposed),
        (ManifestStatus.Approved, ManifestStatus.Proposed),
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
    public void Refuses_to_approve_a_rejected_manifest_without_proposing_it_again()
    {
        ManifestTransitions.CanTransition(ManifestStatus.Rejected, ManifestStatus.Approved).Should().BeFalse();
    }

    [Fact]
    public void Lets_only_a_load_change_send_an_approved_manifest_back()
    {
        // The edge exists, because a change to the load takes it, but a person cannot ask for it.
        ManifestTransitions.CanTransition(ManifestStatus.Approved, ManifestStatus.Proposed).Should().BeTrue();
        ManifestTransitions.CanBeRequested(ManifestStatus.Approved, ManifestStatus.Proposed).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(LegalEdges))]
    public void Lets_a_person_ask_for_every_edge_but_the_load_changes_own(ManifestStatus from, ManifestStatus to) =>
        ManifestTransitions.CanBeRequested(from, to)
            .Should().Be(!(from == ManifestStatus.Approved && to == ManifestStatus.Proposed));

    [Fact]
    public void Refuses_a_transition_to_the_state_a_manifest_is_already_in() =>
        Enum.GetValues<ManifestStatus>()
            .Any(status => ManifestTransitions.CanTransition(status, status))
            .Should().BeFalse();

    [Fact]
    public void Stores_the_three_states_as_the_numbers_the_schema_checks()
    {
        ((int)ManifestStatus.Proposed).Should().Be(0);
        ((int)ManifestStatus.Approved).Should().Be(1);
        ((int)ManifestStatus.Rejected).Should().Be(2);
    }
}
