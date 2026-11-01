using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>
/// Progress marks (ADR 0016, X2): a point is reached, and only a border is crossed. A crossing closes the
/// declarations of the authority whose border it is, and no other.
/// </summary>
public class RoutePointMarksTests
{
    [Theory]
    [InlineData(RoutePointKind.Stop)]
    [InlineData(RoutePointKind.Overnight)]
    [InlineData(RoutePointKind.Border)]
    [InlineData(RoutePointKind.Hub)]
    public void Any_point_can_be_reached(RoutePointKind point) =>
        RoutePointMarks.Check(point, RoutePointMarkKind.Reached).Should().Be(RoutePointMarkCheck.Ok);

    [Fact]
    public void A_border_can_be_crossed() =>
        RoutePointMarks.Check(RoutePointKind.Border, RoutePointMarkKind.Crossed).Should().Be(RoutePointMarkCheck.Ok);

    [Theory]
    [InlineData(RoutePointKind.Stop)]
    [InlineData(RoutePointKind.Overnight)]
    [InlineData(RoutePointKind.Hub)]
    public void Only_a_border_can_be_crossed(RoutePointKind point) =>
        RoutePointMarks.Check(point, RoutePointMarkKind.Crossed).Should().Be(RoutePointMarkCheck.CrossingNeedsBorder);

    [Fact]
    public void Leaving_the_UK_closes_the_GMR_only() =>
        RoutePointMarks.DeclarationsClosedBy(CustomsAuthority.UK).Should().BeEquivalentTo([DeclarationKind.Gmr]);

    [Fact]
    public void Entering_the_EU_closes_the_ENS_and_the_ELO() =>
        RoutePointMarks.DeclarationsClosedBy(CustomsAuthority.EU)
            .Should().BeEquivalentTo([DeclarationKind.Ens, DeclarationKind.Elo]);

    [Fact]
    public void Entering_Ukraine_closes_the_goods_lists() =>
        RoutePointMarks.DeclarationsClosedBy(CustomsAuthority.UA).Should().BeEquivalentTo([DeclarationKind.GoodsList]);

    [Fact]
    public void Every_kind_of_declaration_is_closed_by_exactly_one_authority()
    {
        var closers = Enum.GetValues<DeclarationKind>()
            .Select(kind => Enum.GetValues<CustomsAuthority>()
                .Count(authority => RoutePointMarks.DeclarationsClosedBy(authority).Contains(kind)));

        closers.Should().AllBeEquivalentTo(1);
    }

    [Fact]
    public void A_closed_declaration_is_never_stale() =>
        DeclarationTransitions.CanTransition(DeclarationStatus.Closed, DeclarationStatus.Stale).Should().BeFalse();

    [Fact]
    public void Only_an_accepted_declaration_closes() =>
        Enum.GetValues<DeclarationStatus>()
            .Where(status => DeclarationTransitions.CanTransition(status, DeclarationStatus.Closed))
            .Should().BeEquivalentTo([DeclarationStatus.Accepted]);
}
