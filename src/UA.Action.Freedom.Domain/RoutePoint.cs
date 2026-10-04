namespace UA.Action.Freedom.Domain;

/// <summary>What a point on a convoy's route is for (P15, X2).</summary>
/// <remarks>
/// A point is an <see cref="Overnight"/> stop only because the Dispatcher flagged it: the service never
/// calculates routes or times. A <see cref="Border"/> point is a crossing for one named
/// <see cref="CustomsAuthority"/>.
/// </remarks>
public enum RoutePointKind
{
    Stop = 0,
    Overnight = 1,
    Border = 2,
    Hub = 3,
}

/// <summary>
/// One point on a convoy's route. <see cref="Id"/> survives an edit of the route, so accommodation, progress
/// marks and crossings can point at it.
/// </summary>
public sealed record RoutePoint(
    int Id,
    int Sequence,
    string Name,
    RoutePointKind Kind,
    Address Address,
    CustomsAuthority? Authority = null);
