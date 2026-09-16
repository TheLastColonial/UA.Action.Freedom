using UA.Action.Freedom.Application.Locations;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Dictionary-backed bay persistence so the endpoint tests run without a database.
/// </summary>
internal sealed class InMemoryBayRepository : IBayRepository
{
    private readonly Dictionary<int, BayReadModel> bays = [];

    private int nextId = 1;

    public InMemoryBayRepository(params BayReadModel[] seed)
    {
        foreach (var bay in seed)
        {
            bays[bay.Id] = bay;
            nextId = Math.Max(nextId, bay.Id + 1);
        }
    }

    public BayReadModel? Bay(int id) => bays.GetValueOrDefault(id);

    public Task<BayReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(bays.GetValueOrDefault(id));

    public Task<IReadOnlyList<BayReadModel>> ListByLocationAsync(int locationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BayReadModel>>(
            bays.Values.Where(bay => bay.LocationId == locationId).OrderBy(bay => bay.Code).ToList());

    public Task<bool> CodeExistsAsync(int locationId, string code, int? excludeId, CancellationToken cancellationToken) =>
        Task.FromResult(bays.Values.Any(bay =>
            bay.LocationId == locationId
            && string.Equals(bay.Code, code, StringComparison.Ordinal)
            && bay.Id != excludeId));

    public Task<int> AddAsync(BayReadModel bay, CancellationToken cancellationToken)
    {
        var id = nextId++;
        bays[id] = bay with { Id = id };
        return Task.FromResult(id);
    }

    public Task<bool> UpdateAsync(BayReadModel bay, CancellationToken cancellationToken)
    {
        if (!bays.ContainsKey(bay.Id))
        {
            return Task.FromResult(false);
        }

        bays[bay.Id] = bay;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(bays.Remove(id));
}
