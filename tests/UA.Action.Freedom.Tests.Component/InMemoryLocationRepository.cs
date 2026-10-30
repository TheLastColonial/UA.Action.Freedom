using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Dictionary-backed location persistence so the endpoint tests run without a database.
/// </summary>
internal sealed class InMemoryLocationRepository : ILocationRepository, IRecordsWhoChanged
{
    private readonly Dictionary<int, LocationReadModel> locations = [];

    private readonly ChangeLedger<int> changes = new();

    public void Attach(IChangeAttribution attribution, IPersonRepository people) =>
        changes.Attach(attribution, people);

    private LocationReadModel Read(LocationReadModel location)
    {
        var (name, at) = changes.Of(location.Id);
        return location with { LastChangedByName = name, LastChangedAt = at };
    }

    private int nextId = 1;

    public InMemoryLocationRepository(params LocationReadModel[] seed)
    {
        foreach (var location in seed)
        {
            locations[location.Id] = location;
            nextId = Math.Max(nextId, location.Id + 1);
        }
    }

    public LocationReadModel? Location(int id) => locations.GetValueOrDefault(id);

    public Task<LocationReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(locations.TryGetValue(id, out var location) ? Read(location) : null);

    public Task<IReadOnlyList<LocationReadModel>> ListAsync(
        int page, int pageSize, LocationVisibility visibility, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LocationReadModel>>(
            locations.Values.Where(location => visibility.IsAll || visibility.LocationIds.Contains(location.Id)).OrderBy(location => location.Id).Skip((page - 1) * pageSize).Take(pageSize).Select(Read).ToList());

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(locations.ContainsKey(id));

    public Task<int> AddAsync(LocationReadModel location, CancellationToken cancellationToken)
    {
        var id = nextId++;
        locations[id] = location with { Id = id };
        changes.Stamp(id);
        return Task.FromResult(id);
    }

    public Task<bool> UpdateAsync(LocationReadModel location, CancellationToken cancellationToken)
    {
        if (!locations.ContainsKey(location.Id))
        {
            return Task.FromResult(false);
        }

        locations[location.Id] = location;
        changes.Stamp(location.Id);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        changes.Forget(id);
        return Task.FromResult(locations.Remove(id));
    }
}
