using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>The lifecycle drawn in <c>docs/states/declaration-lifecycle.puml</c>, pinned edge by edge.</summary>
public class DeclarationTransitionsTests
{
    private static readonly (DeclarationStatus From, DeclarationStatus To)[] Diagram =
    [
        (DeclarationStatus.Draft, DeclarationStatus.ReadyToFile),
        (DeclarationStatus.ReadyToFile, DeclarationStatus.Filed),
        (DeclarationStatus.Filed, DeclarationStatus.Accepted),
        (DeclarationStatus.Filed, DeclarationStatus.Refused),
        (DeclarationStatus.Refused, DeclarationStatus.Draft),
        (DeclarationStatus.Filed, DeclarationStatus.Stale),
        (DeclarationStatus.Accepted, DeclarationStatus.Stale),
        (DeclarationStatus.Stale, DeclarationStatus.Withdrawn),
        (DeclarationStatus.Withdrawn, DeclarationStatus.Draft),
        (DeclarationStatus.Accepted, DeclarationStatus.Closed),
    ];

    public static TheoryData<DeclarationStatus, DeclarationStatus> LegalEdges
    {
        get
        {
            var data = new TheoryData<DeclarationStatus, DeclarationStatus>();
            foreach (var (from, to) in Diagram)
            {
                data.Add(from, to);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(LegalEdges))]
    public void Allows_every_edge_the_diagram_draws(DeclarationStatus from, DeclarationStatus to) =>
        DeclarationTransitions.CanTransition(from, to).Should().BeTrue();

    [Fact]
    public void Allows_exactly_the_edges_the_diagram_draws_and_no_others()
    {
        var every = Enum.GetValues<DeclarationStatus>();
        var permitted = every
            .SelectMany(_ => every, (origin, destination) => (origin, destination))
            .Where(edge => DeclarationTransitions.CanTransition(edge.origin, edge.destination))
            .ToHashSet();

        permitted.Should().BeEquivalentTo(Diagram.ToHashSet());
    }

    [Fact]
    public void A_closed_declaration_is_frozen() =>
        Enum.GetValues<DeclarationStatus>()
            .Should().OnlyContain(to => !DeclarationTransitions.CanTransition(DeclarationStatus.Closed, to));

    [Fact]
    public void A_refused_declaration_cannot_be_accepted_without_being_corrected() =>
        DeclarationTransitions.CanTransition(DeclarationStatus.Refused, DeclarationStatus.Accepted).Should().BeFalse();

    [Fact]
    public void Recording_an_ens_goes_straight_to_accepted_because_an_mrn_exists_only_on_acceptance()
    {
        DeclarationTransitions.RecordedStatus(DeclarationKind.Ens).Should().Be(DeclarationStatus.Accepted);
        DeclarationTransitions.RecordedStatus(DeclarationKind.Gmr).Should().Be(DeclarationStatus.Filed);
        DeclarationTransitions.RecordedStatus(DeclarationKind.GoodsList).Should().Be(DeclarationStatus.Filed);
    }

    [Fact]
    public void Only_an_accepted_declaration_is_current() =>
        Enum.GetValues<DeclarationStatus>().Where(DeclarationTransitions.IsCurrent)
            .Should().ContainSingle().Which.Should().Be(DeclarationStatus.Accepted);
}
