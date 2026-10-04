using UA.Action.Freedom.Application.Locations;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// A list-backed <see cref="ILoaderAssignmentRepository"/>. Like the SQL it mirrors, only an open row (no
/// <c>Until</c>) means a Loader manages a location, removal closes the row rather than deleting it, and a person has at
/// most one open assignment per location.
/// </summary>
internal sealed class InMemoryLoaderAssignmentRepository : ILoaderAssignmentRepository
{
    private readonly List<LoaderAssignmentReadModel> assignments = [];

    private readonly Func<Guid, string> nameOf;

    public InMemoryLoaderAssignmentRepository(Func<Guid, string>? nameOf = null) =>
        this.nameOf = nameOf ?? (_ => "Test Loader");

    /// <summary>Seeds an open assignment, as an Administrator's earlier act.</summary>
    public InMemoryLoaderAssignmentRepository Managing(Guid personId, int locationId)
    {
        AssignAsync(locationId, personId, DateTime.UtcNow, CancellationToken.None).GetAwaiter().GetResult();
        return this;
    }

    public Task<IReadOnlyList<LoaderAssignmentReadModel>> HistoryAsync(int locationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LoaderAssignmentReadModel>>(
            [.. assignments.Where(a => a.LocationId == locationId).OrderByDescending(a => a.Id)]);

    public Task<AssignLoaderResult> AssignAsync(
        int locationId, Guid personId, DateTime at, CancellationToken cancellationToken)
    {
        if (assignments.Exists(a => a.LocationId == locationId && a.PersonId == personId && a.Until is null))
        {
            return Task.FromResult(AssignLoaderResult.AlreadyAssigned);
        }

        assignments.Add(new LoaderAssignmentReadModel(assignments.Count + 1, locationId, personId, nameOf(personId), at, null));
        return Task.FromResult(AssignLoaderResult.Assigned);
    }

    public Task<bool> UnassignAsync(int locationId, Guid personId, DateTime at, CancellationToken cancellationToken)
    {
        var open = assignments.FindIndex(a => a.LocationId == locationId && a.PersonId == personId && a.Until is null);
        if (open < 0)
        {
            return Task.FromResult(false);
        }

        assignments[open] = assignments[open] with { Until = at };
        return Task.FromResult(true);
    }

    public Task<bool> ManagesAsync(Guid personId, int locationId, CancellationToken cancellationToken) =>
        Task.FromResult(assignments.Exists(a => a.LocationId == locationId && a.PersonId == personId && a.Until is null));

    public Task<IReadOnlyList<int>> ManagedLocationIdsAsync(Guid personId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<int>>(
            [.. assignments.Where(a => a.PersonId == personId && a.Until is null).Select(a => a.LocationId).Order()]);
}
