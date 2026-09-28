namespace UA.Action.Freedom.Api.Configuration;

/// <summary>
/// What an ICS2 Entry Summary Declaration needs that neither the manifest nor the convoy knows.
/// </summary>
/// <remarks>
/// Nothing here reaches ICS2. Freedom does not submit declarations — the Shared Trader Interface
/// speaks eDelivery AS4, and an always-on inbound access point is what
/// <c>docs/recommendations.md</c> §4.1 declines — so these are the fields a Ground Officer would
/// otherwise have to remember while filing in the EU Customs Trader Portal
/// (<c>docs/adr/0003</c>).
///
/// <para>
/// The declarant's EORI is deliberately <em>not</em> duplicated here: it is already
/// <see cref="CustomsOptions.HaulierEori"/>, and the same charity is declarant, carrier and consignor
/// on these movements. Two settings for one EORI is two settings to get out of step.
/// </para>
/// </remarks>
public sealed class EnsOptions
{
    public const string SectionName = "Ens";

    /// <summary>The name of the party sending the goods.</summary>
    public string ConsignorName { get; set; } = string.Empty;

    /// <summary>
    /// The customs office where the goods first enter the EU, as an eight-character office
    /// reference — <c>FR620001</c> for Calais.
    /// </summary>
    /// <remarks>
    /// A property of the crossing rather than of the application, exactly as
    /// <see cref="CustomsOptions.RouteId"/> is: see <c>docs/gotchas-and-open-questions.md</c> §9. Left
    /// as configuration while every convoy crosses the same way; if a second crossing point appears it
    /// moves onto <c>dbo.Convoy</c> beside <c>CrossingMode</c>. It is also non-amendable in ICS2, so
    /// getting it wrong means invalidating the declaration and filing again.
    /// </remarks>
    public string OfficeOfFirstEntry { get; set; } = string.Empty;
}
