namespace UA.Action.Freedom.Domain;

/// <summary>What the Convoy Leader reports about a route point (ADR 0016). Stored: do not reorder.</summary>
public enum RoutePointMarkKind
{
    Reached = 0,
    Crossed = 1,
}

public enum RoutePointMarkCheck
{
    Ok,
    CrossingNeedsBorder,
}

/// <summary>
/// One report of progress. <see cref="Vin"/> is null for the whole convoy and set where vehicles split.
/// <see cref="OnBehalfOfLeader"/> is true when somebody other than the Convoy Leader recorded it (O27); only the time
/// it was entered is kept, never where anyone was (O20).
/// </summary>
public sealed record RoutePointMark(
    int ConvoyId,
    int RoutePointId,
    RoutePointMarkKind Kind,
    string? Vin,
    Guid? RecordedBy,
    DateTime RecordedAt,
    bool OnBehalfOfLeader);

public static class RoutePointMarks
{
    /// <summary>Any point can be reached, and a crossing is a border's alone (X2).</summary>
    public static RoutePointMarkCheck Check(RoutePointKind point, RoutePointMarkKind mark) =>
        mark == RoutePointMarkKind.Crossed && point != RoutePointKind.Border
            ? RoutePointMarkCheck.CrossingNeedsBorder
            : RoutePointMarkCheck.Ok;

    /// <summary>The declarations a crossing of this authority's border closes: leaving the UK, entering France, entering Ukraine.</summary>
    public static IReadOnlyList<DeclarationKind> DeclarationsClosedBy(CustomsAuthority authority) => authority switch
    {
        CustomsAuthority.UK => [DeclarationKind.Gmr],
        CustomsAuthority.EU => [DeclarationKind.Ens, DeclarationKind.Elo],
        _ => [DeclarationKind.GoodsList],
    };
}
