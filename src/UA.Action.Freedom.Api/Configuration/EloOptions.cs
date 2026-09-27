namespace UA.Action.Freedom.Api.Configuration;

/// <summary>
/// What the French customs logistics envelope needs that the manifest itself does not know.
/// </summary>
/// <remarks>
/// Only the declaration identifiers live here. The rest of an envelope — the crossing direction, the
/// lorry type and the TIR/ATA flags — is a ruling about how Ukrainian Action's convoys travel, so it
/// lives in the domain as <c>EloCrossingProfile.HumanitarianAidToUkraine</c> and not in
/// configuration, where it could be changed by an environment variable without anyone reading why.
///
/// <para>
/// The worker's own base URL and credentials are configured on the worker, under its own
/// <c>Elo</c> section. Nothing here reaches French customs; this project only enqueues.
/// </para>
/// </remarks>
public sealed class EloOptions
{
    public const string SectionName = "Elo";

    /// <summary>
    /// A stand-in declaration identifier, used until Freedom can obtain a real one.
    /// </summary>
    /// <remarks>
    /// <strong>This is a placeholder, and a real submission will be refused while it is in use.</strong>
    ///
    /// <para>
    /// An ELO references customs declarations rather than describing goods, and the identifiers come
    /// from systems Freedom does not yet talk to: an ENS from ICS2, a transit MRN from DELTA-T. Under
    /// cross-functional rule ENV_CTR_RG08 a loaded lorry arriving in France under TIR/ATA must name at
    /// least one ENS, so French customs answers a placeholder with FONC-ERR-004
    /// ("Format de déclaration incorrect") and the worker dead-letters it.
    /// </para>
    ///
    /// <para>
    /// That is deliberate sequencing rather than an oversight: it makes the whole durable path —
    /// enqueue, submit, store, serve — real and testable against the local stub, leaving one known
    /// gap. Integrating ICS2 closes it, and at that point the identifiers become per-manifest data
    /// and this setting goes away. See <c>docs/gotchas-and-open-questions.md</c> §8 and
    /// <c>docs/schemas/edi/onboarding.md</c>.
    /// </para>
    /// </remarks>
    public string PlaceholderDeclarationIdentifier { get; set; } = string.Empty;
}
