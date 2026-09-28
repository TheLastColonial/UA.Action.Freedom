using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>
/// The pairing information Ukrainian Action's convoys declare on an ELO envelope.
/// </summary>
/// <remarks>
/// Every one of these flags is a ruling rather than a preference, and each one changes what French
/// customs demands in the same envelope. Pinning them here means a flag cannot be flipped quietly:
/// the test that fails says which rule was relied on and where it came from.
/// </remarks>
public class EloCrossingProfileTests
{
    /// <summary>
    /// UK to France is an arrival into the EU. Version 1.1.0 of the service contract renamed the
    /// values from ENTREE/SORTIE to IMPORT/EXPORT, which reads as an exporter's word for a
    /// departure and is not: the crossing direction is named from the French border's side.
    /// </summary>
    [Fact]
    public void A_crossing_from_the_uk_to_france_is_an_import()
    {
        EloCrossingProfile.HumanitarianAidToUkraine.Direction
            .Should().Be(EloCrossingDirection.Import);
    }

    /// <summary>
    /// The vehicles are themselves part of the aid and they travel loaded, so the transport unit is
    /// PLEIN. An empty lorry may carry no formalities at all, which is a different envelope
    /// entirely.
    /// </summary>
    [Fact]
    public void The_lorry_is_declared_loaded()
    {
        EloCrossingProfile.HumanitarianAidToUkraine.LorryType.Should().Be(EloLorryType.Loaded);
    }

    /// <summary>
    /// Issue #24, quoting French Customs guidance: "For emergency humanitarian aid destined for
    /// Ukraine, select TIR/ATA without transport contract." Under cross-functional rule
    /// ENV_CTR_RG08 that is what reduces the envelope's requirement to a single ENS, instead of the
    /// ENS plus customs-clearance formality a loaded lorry outside TIR/ATA must carry.
    /// </summary>
    [Fact]
    public void Aid_travels_under_tir_ata_without_a_transport_contract()
    {
        var profile = EloCrossingProfile.HumanitarianAidToUkraine;

        profile.TirAta.Should().BeTrue();
        profile.HasTransportContract.Should().BeFalse();
    }

    /// <summary>
    /// The remaining flags describe cargo Ukrainian Action does not carry. They matter because each
    /// true value pulls in a further border regime — an SPS consignment needs a border control post
    /// appointment, and fishery products a catch certificate.
    /// </summary>
    [Fact]
    public void Nothing_else_is_claimed_about_the_cargo()
    {
        var profile = EloCrossingProfile.HumanitarianAidToUkraine;

        profile.Postal.Should().BeFalse();
        profile.EmptyPackaging.Should().BeFalse();
        profile.SanitaryOrPhytosanitary.Should().BeFalse();
        profile.FisheryProducts.Should().BeFalse();
    }

    /// <summary>
    /// ENV_CTR_RG08 again, from the other side: an envelope for an empty lorry must carry no
    /// formalities and must claim no TIR/ATA, SPS or fishery goods. Modelling the question as a
    /// method keeps the rule next to the flags it reads rather than in whichever caller happens to
    /// build the request.
    /// </summary>
    [Fact]
    public void A_loaded_lorry_needs_at_least_one_declaration_and_an_empty_one_needs_none()
    {
        EloCrossingProfile.HumanitarianAidToUkraine.RequiresADeclaration.Should().BeTrue();

        var empty = EloCrossingProfile.HumanitarianAidToUkraine with
        {
            LorryType = EloLorryType.Empty,
            TirAta = false,
        };

        empty.RequiresADeclaration.Should().BeFalse();
    }
}
