using System.Text.RegularExpressions;

namespace UA.Action.Freedom.Domain;

/// <summary>
/// The Movement Reference Number ICS2 issues when it accepts an Entry Summary Declaration.
/// </summary>
/// <remarks>
/// This is the identifier the whole border chain hangs off. An ELO cannot be created without it —
/// under ENV_CTR_RG08 a TIR/ATA lorry's envelope needs exactly one formality, and this is it — and
/// HMRC's GVMS accepts the same value as a customs declaration identifier on a Goods Movement
/// Record. So it is recorded once, per manifest, and read by both.
///
/// <para>
/// A plain wrapper, like the other identity types: well-formedness is a question asked of a
/// candidate string at the edges, by the request validator and by the handler that records one, not
/// an invariant of the type. <see cref="IsWellFormed"/> is the single answer both use.
/// </para>
/// </remarks>
/// <param name="Value">The eighteen-character reference, as ICS2 issued it.</param>
public sealed record EnsMrn(string Value)
{
    /// <summary>How many characters an MRN has.</summary>
    public const int Length = 18;

    // Two digits of the year of acceptance, the ISO alpha-2 code of the declaring country, then
    // thirteen characters of reference and a check character. The last fourteen are matched as one
    // group on purpose: the split between reference and check character is not something Freedom
    // needs to know, and pretending otherwise would invite verifying a check it deliberately does
    // not verify.
    private static readonly Regex WellFormed = new(
        "^[0-9]{2}[A-Z]{2}[0-9A-Z]{14}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Whether a candidate string has the shape of an MRN.
    /// </summary>
    /// <remarks>
    /// The shape only. The check character is not verified: implementing that algorithm wrongly
    /// would refuse MRNs ICS2 has already issued, stranding a convoy over a bug of ours, where
    /// letting a typo through means customs refuses the envelope and says so. Case matters, because
    /// an MRN that differs only in case is a different string to every system asked to match it.
    /// </remarks>
    public static bool IsWellFormed(string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate) && WellFormed.IsMatch(candidate);

    public override string ToString() => this.Value;
}

/// <summary>
/// How a convoy crosses the Channel.
/// </summary>
/// <remarks>
/// A property of the crossing rather than the cargo, and it decides two things an ENS cannot be
/// filed without: the mode of transport, and whether a vessel has to be named.
/// </remarks>
public enum ChannelCrossing
{
    /// <summary>A ro-ro ferry sailing. The vessel is the active means of transport.</summary>
    Ferry,

    /// <summary>LeShuttle through the Channel Tunnel. The lorry is the active means of transport.</summary>
    Shuttle,
}

/// <summary>
/// The ENS mode-of-transport code for a crossing, and what that code demands.
/// </summary>
/// <remarks>
/// The mapping is not the obvious one and getting it wrong is expensive, because mode of transport
/// is a non-amendable field: a mistake means invalidating the declaration and filing a new one, not
/// correcting it.
/// </remarks>
public static class EnsTransportMode
{
    /// <summary>Maritime. A lorry accompanied on a ferry crosses as maritime.</summary>
    public const int Maritime = 1;

    /// <summary>
    /// Rail. Never used here: the Brexit Smart Border does not accept it, and a lorry on the shuttle
    /// is declared as road even though it travels on a train.
    /// </summary>
    public const int Rail = 2;

    /// <summary>Road.</summary>
    public const int Road = 3;

    /// <summary>The mode-of-transport code to declare for a crossing.</summary>
    public static int For(ChannelCrossing crossing) => crossing switch
    {
        ChannelCrossing.Ferry => Maritime,
        ChannelCrossing.Shuttle => Road,
        _ => throw new ArgumentOutOfRangeException(nameof(crossing), crossing, "Unknown crossing."),
    };

    /// <summary>
    /// Whether the crossing names a vessel as its active means of transport, and so needs an IMO
    /// number.
    /// </summary>
    /// <remarks>
    /// The IMO has to come from the official list for the route, and once declared it may never be
    /// amended. A shuttle crossing names the lorry's own registration instead, which Freedom already
    /// holds.
    /// </remarks>
    public static bool NeedsAVessel(ChannelCrossing crossing) => crossing == ChannelCrossing.Ferry;
}

/// <summary>
/// UN/ECE Recommendation 21 package type codes, for the kinds of package Ukrainian Action ships.
/// </summary>
/// <remarks>
/// A constant rather than a column on <c>dbo.Box</c>: a box is a box, and every container on a
/// manifest is one. If pallets or crates ever travel, this is the seam that has to become data.
/// </remarks>
public static class EnsPackaging
{
    /// <summary>Box.</summary>
    public const string Box = "BX";
}

/// <summary>
/// Commodity codes that apply to Ukrainian Action's cargo.
/// </summary>
public static class EnsCommodity
{
    /// <summary>
    /// Goods for humanitarian relief, 9919 00 00, carried without its display spacing because that
    /// is what a declaration field takes.
    /// </summary>
    /// <remarks>
    /// French Customs' guidance for this traffic, recorded in issue #24 and noted in
    /// <c>docs/schemas/edi/onboarding.md</c>. It belongs on the declaration rather than the envelope,
    /// which is why it appears now and not when only the ELO existed. ICS2 requires at least six
    /// digits per goods item.
    /// </remarks>
    public const string HumanitarianAid = "99190000";
}
