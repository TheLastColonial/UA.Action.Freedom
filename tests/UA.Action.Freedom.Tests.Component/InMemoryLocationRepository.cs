using UA.Action.Freedom.Application.Locations;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Dictionary-backed location persistence so the endpoint tests run without a database.
/// </summary>
internal sealed class InMemoryLocationRepository : ILocationRepository
{
    private readonly Dictionary<int, LocationReadModel> locations = [];

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
        Task.FromResult(locations.GetValueOrDefault(id));

    public Task<IReadOnlyList<LocationReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LocationReadModel>>(
            locations.Values.OrderBy(location => location.Id).Skip((page - 1) * pageSize).Take(pageSize).ToList());

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(locations.ContainsKey(id));

    public Task<int> AddAsync(LocationReadModel location, CancellationToken cancellationToken)
    {
        var id = nextId++;
        locations[id] = location with { Id = id };
        return Task.FromResult(id);
    }

    public Task<bool> UpdateAsync(LocationReadModel location, CancellationToken cancellationToken)
    {
        if (!locations.ContainsKey(location.Id))
        {
            return Task.FromResult(false);
        }

        locations[location.Id] = location;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(locations.Remove(id));
}
