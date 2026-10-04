namespace UA.Action.Freedom.Application.Locations;

/// <summary>Persistence port for <c>dbo.LoaderLocationAssignment</c>: which locations a Loader manages.</summary>
public interface ILoaderAssignmentRepository
{
    /// <summary>True while the person has an open assignment to the location.</summary>
    Task<bool> ManagesAsync(Guid personId, int locationId, CancellationToken cancellationToken);

    /// <summary>The locations the person currently manages, and no others.</summary>
    Task<IReadOnlyList<int>> ManagedLocationIdsAsync(Guid personId, CancellationToken cancellationToken);
}
