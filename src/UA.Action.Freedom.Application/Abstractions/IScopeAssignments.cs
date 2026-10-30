using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Locations;

namespace UA.Action.Freedom.Application.Abstractions;

/// <summary>
/// The assignments that give a scoped role its reach (ADR 0010): who leads which convoy, and which locations a
/// Loader manages. Read on every request, never from a token and never kept beyond it, so a reassignment takes
/// effect on the caller's next call.
/// </summary>
public interface IScopeAssignments
{
    Task<bool> IsCurrentLeaderAsync(int convoyId, Guid personId, CancellationToken cancellationToken);

    Task<bool> ManagesLocationAsync(Guid personId, int locationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<int>> ManagedLocationIdsAsync(Guid personId, CancellationToken cancellationToken);

    Task<IReadOnlyList<int>> LedConvoyIdsAsync(Guid personId, CancellationToken cancellationToken);
}

/// <summary>Composes <see cref="IScopeAssignments"/> from the two stores that hold the assignments.</summary>
public sealed class ScopeAssignments(IConvoyLeaderRepository leaders, ILoaderAssignmentRepository loaders)
    : IScopeAssignments
{
    public Task<bool> IsCurrentLeaderAsync(int convoyId, Guid personId, CancellationToken cancellationToken) =>
        leaders.IsCurrentLeaderAsync(convoyId, personId, cancellationToken);

    public Task<bool> ManagesLocationAsync(Guid personId, int locationId, CancellationToken cancellationToken) =>
        loaders.ManagesAsync(personId, locationId, cancellationToken);

    public Task<IReadOnlyList<int>> ManagedLocationIdsAsync(Guid personId, CancellationToken cancellationToken) =>
        loaders.ManagedLocationIdsAsync(personId, cancellationToken);

    public Task<IReadOnlyList<int>> LedConvoyIdsAsync(Guid personId, CancellationToken cancellationToken) =>
        leaders.LedConvoyIdsAsync(personId, cancellationToken);
}

/// <summary>
/// Which boxes and locations a caller may see in a list. There is deliberately no default: a list handler has to
/// decide, so a new list cannot ship unscoped. The empty set with no unlocated boxes is nothing at all.
/// </summary>
public sealed record LocationVisibility
{
    private LocationVisibility()
    {
    }

    public bool IsAll { get; private init; }

    public IReadOnlyList<int> LocationIds { get; private init; } = [];

    /// <summary>Boxes that are not at any location yet (expected boxes) are visible too.</summary>
    public bool IncludeUnlocated { get; private init; }

    public static LocationVisibility All { get; } = new() { IsAll = true, IncludeUnlocated = true };

    public static LocationVisibility Only(IEnumerable<int> locationIds, bool includeUnlocated) =>
        new() { LocationIds = [.. locationIds.Distinct()], IncludeUnlocated = includeUnlocated };

    public bool Allows(int? locationId) =>
        IsAll || (locationId is int id ? LocationIds.Contains(id) : IncludeUnlocated);
}
