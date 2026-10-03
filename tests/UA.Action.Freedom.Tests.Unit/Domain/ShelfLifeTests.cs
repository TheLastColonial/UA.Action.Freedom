using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>
/// An expired item blocks validation of its box and a short shelf life warns (D1, D25). The thresholds
/// are unverified, so they are held on the category's rule rather than in the assessment.
/// </summary>
public class ShelfLifeTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    private static readonly ShelfLifeRule SixMonths = new(WarnWithinDays: 180);

    private static readonly ShelfLifeRule NinetyDays = new(WarnWithinDays: 90);

    [Fact]
    public void An_item_with_no_expiry_is_fine()
    {
        ShelfLife.Assess(expiresOn: null, SixMonths, Today).Should().Be(ShelfLifeStatus.Fine);
    }

    [Fact]
    public void An_item_that_expired_yesterday_is_expired()
    {
        ShelfLife.Assess(Today.AddDays(-1), SixMonths, Today).Should().Be(ShelfLifeStatus.Expired);
    }

    [Fact]
    public void An_item_that_expires_today_is_still_usable_but_short()
    {
        ShelfLife.Assess(Today, SixMonths, Today).Should().Be(ShelfLifeStatus.Short);
    }

    [Theory]
    [InlineData(179, ShelfLifeStatus.Short)]
    [InlineData(180, ShelfLifeStatus.Short)]
    [InlineData(181, ShelfLifeStatus.Fine)]
    public void Medicine_is_short_within_six_months(int daysLeft, ShelfLifeStatus expected)
    {
        ShelfLife.Assess(Today.AddDays(daysLeft), SixMonths, Today).Should().Be(expected);
    }

    [Theory]
    [InlineData(90, ShelfLifeStatus.Short)]
    [InlineData(91, ShelfLifeStatus.Fine)]
    public void The_threshold_belongs_to_the_rule_not_the_assessment(int daysLeft, ShelfLifeStatus expected)
    {
        ShelfLife.Assess(Today.AddDays(daysLeft), NinetyDays, Today).Should().Be(expected);
    }

    [Fact]
    public void A_category_with_no_rule_is_never_short_but_can_still_be_expired()
    {
        ShelfLife.Assess(Today.AddDays(1), ShelfLifeRule.None, Today).Should().Be(ShelfLifeStatus.Fine);
        ShelfLife.Assess(Today.AddDays(-1), ShelfLifeRule.None, Today).Should().Be(ShelfLifeStatus.Expired);
    }

    [Fact]
    public void The_value_sources_are_stable_because_they_are_stored()
    {
        ((int)ValueSource.Donor).Should().Be(0);
        ((int)ValueSource.Estimate).Should().Be(1);
        ((int)ValueSource.Purchased).Should().Be(2);
    }

    [Fact]
    public void The_customs_authorities_are_stable_because_they_are_stored()
    {
        ((int)CustomsAuthority.UK).Should().Be(0);
        ((int)CustomsAuthority.EU).Should().Be(1);
        ((int)CustomsAuthority.UA).Should().Be(2);
    }
}
