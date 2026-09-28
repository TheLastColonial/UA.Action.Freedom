using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>
/// The facts about an ICS2 Entry Summary Declaration that Freedom has to get right before a convoy
/// can cross.
/// </summary>
/// <remarks>
/// Each of these is a customs ruling rather than a preference, and each one is pinned here for the
/// same reason <see cref="EloCrossingProfileTests"/> pins the crossing profile: the test that fails
/// says which rule was relied on and where it came from.
/// </remarks>
public class EnsDeclarationTests
{
    /// <summary>
    /// An MRN is eighteen characters: two digits of the year of acceptance, the ISO alpha-2 code of
    /// the declaring country, thirteen characters of reference and a check character. This is the
    /// shape French customs pairs an ELO against, so a malformed one recorded here becomes a refused
    /// envelope later.
    /// </summary>
    [Fact]
    public void A_well_formed_mrn_is_a_year_a_country_and_a_reference()
    {
        EnsMrn.IsWellFormed("25FR17551780961AT5").Should().BeTrue();
    }

    /// <summary>
    /// Everything that is not that shape. Lower case is included deliberately: the declaring country
    /// and the reference are upper case, and an MRN that differs only in case is a different string
    /// to every system that will be asked to match it.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("25FR17551780961AT")]      // seventeen characters
    [InlineData("25FR17551780961AT55")]    // nineteen
    [InlineData("25fr17551780961at5")]     // lower case
    [InlineData("2XFR17551780961AT5")]     // the year is not two digits
    [InlineData("2517551780961AT5FR")]     // the country is not two letters
    [InlineData("25FR17551780961A-5")]     // the reference is not alphanumeric
    public void Anything_else_is_not_an_mrn(string? candidate)
    {
        EnsMrn.IsWellFormed(candidate).Should().BeFalse();
    }

    /// <summary>
    /// The check character is deliberately not verified. Freedom checks the shape and lets customs
    /// settle the rest: implementing the check algorithm wrongly would reject MRNs that ICS2 has
    /// already issued, which is a worse failure than passing a typo through to an authority that
    /// will catch it.
    /// </summary>
    [Fact]
    public void The_check_character_is_not_verified_here()
    {
        EnsMrn.IsWellFormed("25FR17551780961AT0").Should().BeTrue();
    }

    /// <summary>
    /// Mode of transport describes the crossing, not the cargo. A lorry on a ferry is maritime even
    /// though a lorry drove on; a lorry on the shuttle is road even though it travels on a train.
    /// </summary>
    [Theory]
    [InlineData(ChannelCrossing.Ferry, 1)]
    [InlineData(ChannelCrossing.Shuttle, 3)]
    public void The_mode_of_transport_follows_the_crossing(ChannelCrossing crossing, int expected)
    {
        EnsTransportMode.For(crossing).Should().Be(expected);
    }

    /// <summary>
    /// Code 2, rail, is not accepted at the Brexit Smart Border — a shuttle crossing is declared as
    /// road. Asserted across every crossing rather than against one value, so a new crossing cannot
    /// quietly introduce it.
    /// </summary>
    [Fact]
    public void Rail_is_never_the_mode_of_transport()
    {
        var modes = Enum.GetValues<ChannelCrossing>().Select(EnsTransportMode.For);

        modes.Should().NotContain(EnsTransportMode.Rail);
    }

    /// <summary>
    /// A ferry crossing names the vessel as the active means of transport, so it needs an IMO
    /// number; the shuttle names the lorry's own plate, which Freedom always has.
    /// </summary>
    [Theory]
    [InlineData(ChannelCrossing.Ferry, true)]
    [InlineData(ChannelCrossing.Shuttle, false)]
    public void Only_a_ferry_crossing_needs_a_vessel(ChannelCrossing crossing, bool expected)
    {
        EnsTransportMode.NeedsAVessel(crossing).Should().Be(expected);
    }

    /// <summary>
    /// A box of aid is declared with UN/ECE Recommendation 21 package type BX. Every container
    /// Ukrainian Action ships is a box, so this is a constant rather than a column on
    /// <c>dbo.Box</c>.
    /// </summary>
    [Fact]
    public void Aid_is_packed_in_boxes()
    {
        EnsPackaging.Box.Should().Be("BX");
    }

    /// <summary>
    /// Commodity code 9919 00 00 covers humanitarian aid, per the French Customs guidance recorded
    /// in issue #24 and noted in <c>docs/schemas/edi/onboarding.md</c>. It belongs on the
    /// declaration, which is why it appears now and did not when only the envelope existed.
    /// </summary>
    [Fact]
    public void Humanitarian_aid_has_a_commodity_code_of_its_own()
    {
        EnsCommodity.HumanitarianAid.Should().Be("99190000");
    }

    /// <summary>
    /// The code is carried without its display spacing, because that is what a declaration field
    /// takes, and it is at least the six digits ICS2 requires per goods item.
    /// </summary>
    [Fact]
    public void A_commodity_code_is_at_least_six_digits_and_carries_no_spaces()
    {
        EnsCommodity.HumanitarianAid.Should().MatchRegex("^[0-9]{6,}$");
    }
}
