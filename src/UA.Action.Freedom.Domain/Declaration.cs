namespace UA.Action.Freedom.Domain;

/// <summary>The instruments declared to a customs authority about one vehicle's load.</summary>
public enum DeclarationKind
{
    Gmr = 0,
    Ens = 1,
    Elo = 2,
    GoodsList = 3,
}

/// <summary>Where a declaration is in docs/states/declaration-lifecycle.puml.</summary>
public enum DeclarationStatus
{
    Draft = 0,
    ReadyToFile = 1,
    Filed = 2,
    Accepted = 3,
    Refused = 4,
    Stale = 5,
    Withdrawn = 6,
    Closed = 7,
}

/// <summary>
/// A statement to a customs authority about one vehicle's load (ADR 0005). A goods list is one per
/// receiver, so <see cref="ReceiverRef"/> is set for that kind only.
/// </summary>
public sealed record Declaration(
    int Id,
    int ConvoyId,
    string Vin,
    DeclarationKind Kind,
    DeclarationStatus Status,
    Guid? ReceiverRef = null,
    string? Reference = null,
    string? ReasonCode = null);

/// <summary>
/// The edges of the declaration lifecycle, held as data like <see cref="ManifestTransitions"/>.
/// </summary>
/// <remarks>
/// <see cref="DeclarationStatus.Stale"/> is set only by derived staleness (plan 09) and
/// <see cref="DeclarationStatus.Closed"/> only by a border crossing (plan 18); this plan defines
/// both edges and never takes them.
/// </remarks>
public static class DeclarationTransitions
{
    private static readonly HashSet<(DeclarationStatus From, DeclarationStatus To)> Allowed =
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

    public static bool CanTransition(DeclarationStatus from, DeclarationStatus to) => Allowed.Contains((from, to));

    /// <summary>
    /// The ENS has no separate filed state: an MRN exists only on acceptance, so recording one goes
    /// straight to <see cref="DeclarationStatus.Accepted"/>.
    /// </summary>
    public static DeclarationStatus RecordedStatus(DeclarationKind kind) =>
        kind == DeclarationKind.Ens ? DeclarationStatus.Accepted : DeclarationStatus.Filed;

    /// <summary>Only an accepted declaration counts as current.</summary>
    public static bool IsCurrent(DeclarationStatus status) => status == DeclarationStatus.Accepted;
}
