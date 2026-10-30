namespace UA.Action.Freedom.Application.Locations;

/// <summary>One stretch of a Loader managing a location. <see cref="Until"/> is null while it is open.</summary>
public sealed record LoaderAssignmentReadModel(
    int Id,
    int LocationId,
    Guid PersonId,
    string PersonName,
    DateTime From,
    DateTime? Until,
    string? LastChangedByName = null,
    DateTime? LastChangedAt = null);

public enum AssignLoaderResult
{
    Assigned,
    AlreadyAssigned
}

/// <summary>Persistence port for <c>dbo.LoaderLocationAssignment</c>: which locations a Loader manages (O14, O31).</summary>
public interface ILoaderAssignmentRepository
{
    /// <summary>Every assignment to the location, open or closed, newest first.</summary>
    Task<IReadOnlyList<LoaderAssignmentReadModel>> HistoryAsync(int locationId, CancellationToken cancellationToken);

    /// <summary>Opens an assignment unless the person already has an open one at the location.</summary>
    Task<AssignLoaderResult> AssignAsync(int locationId, Guid personId, DateTime at, CancellationToken cancellationToken);

    /// <summary>Closes the open assignment, keeping the row. False when there was none.</summary>
    Task<bool> UnassignAsync(int locationId, Guid personId, DateTime at, CancellationToken cancellationToken);

    /// <summary>True while the person has an open assignment to the location.</summary>
    Task<bool> ManagesAsync(Guid personId, int locationId, CancellationToken cancellationToken);

    /// <summary>The locations the person currently manages, and no others.</summary>
    Task<IReadOnlyList<int>> ManagedLocationIdsAsync(Guid personId, CancellationToken cancellationToken);
}
