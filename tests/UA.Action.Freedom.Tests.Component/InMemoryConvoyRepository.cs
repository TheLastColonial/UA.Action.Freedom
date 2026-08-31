using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// A dictionary-backed <see cref="IConvoyRepository"/> so the endpoint tests run without a
/// database. The Dapper implementation is covered separately by the integration tests.
/// </summary>
internal sealed class InMemoryConvoyRepository : IConvoyRepository
{
    private readonly Dictionary<int, ConvoyReadModel> convoys = [];
    private readonly Dictionary<int, List<RouteStopReadModel>> routes = [];

    /// <summary>VIN to the convoy it is on, standing in for <c>dbo.Vehicle.ConvoyId</c>.</summary>
    private readonly Dictionary<string, int?> vehicles = new(StringComparer.OrdinalIgnoreCase);

    private int nextId = 1;

    public InMemoryConvoyRepository(params ConvoyReadModel[] seed)
    {
        foreach (var convoy in seed)
        {
            convoys[convoy.Id] = convoy;
            nextId = Math.Max(nextId, convoy.Id + 1);
        }
    }

    /// <summary>Adds a vehicle that exists but is on no convoy, so it can be assigned to one.</summary>
    public InMemoryConvoyRepository WithVehicle(string vin, int? onConvoy = null)
    {
        vehicles[vin] = onConvoy;
        return this;
    }

    public int Count => convoys.Count;

    public int? ConvoyOf(string vin) => vehicles.GetValueOrDefault(vin);

    public IReadOnlyList<RouteStopReadModel> RouteOf(int convoyId) =>
        routes.GetValueOrDefault(convoyId, []);

    public Task<ConvoyReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(convoys.GetValueOrDefault(id));

    public Task<IReadOnlyList<ConvoyReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConvoyReadModel>>(
            convoys.Values
                .OrderByDescending(convoy => convoy.Start)
                .ThenByDescending(convoy => convoy.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList());

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(convoys.ContainsKey(id));

    public Task<int> AddAsync(DateTime start, DateTime expectedEnd, CancellationToken cancellationToken)
    {
        var id = nextId++;
        convoys[id] = new ConvoyReadModel(id, start, expectedEnd, TruckListPublishedAt: null);
        return Task.FromResult(id);
    }

    public Task<bool> UpdateAsync(ConvoyReadModel convoy, CancellationToken cancellationToken)
    {
        if (!convoys.TryGetValue(convoy.Id, out var existing))
        {
            return Task.FromResult(false);
        }

        // Mirrors the SQL, which does not touch TruckListPublishedAt on an ordinary update.
        convoys[convoy.Id] = convoy with { TruckListPublishedAt = existing.TruckListPublishedAt };
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        routes.Remove(id);

        foreach (var vin in vehicles.Where(entry => entry.Value == id).Select(entry => entry.Key).ToList())
        {
            vehicles[vin] = null;
        }

        return Task.FromResult(convoys.Remove(id));
    }

    public Task<IReadOnlyList<RouteStopReadModel>> GetRouteAsync(int convoyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RouteStopReadModel>>(routes.GetValueOrDefault(convoyId, []));

    public Task ReplaceRouteAsync(
        int convoyId, IReadOnlyList<RouteStopReadModel> stops, CancellationToken cancellationToken)
    {
        routes[convoyId] = [.. stops];
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ConvoyVehicleReadModel>> ListVehiclesAsync(int convoyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConvoyVehicleReadModel>>(
            vehicles
                .Where(entry => entry.Value == convoyId)
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new ConvoyVehicleReadModel(entry.Key, "AB12CDE", 1_400))
                .ToList());

    public Task<bool> AssignVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        if (!vehicles.ContainsKey(vin))
        {
            return Task.FromResult(false);
        }

        vehicles[vin] = convoyId;
        return Task.FromResult(true);
    }

    public Task<bool> UnassignVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        if (vehicles.GetValueOrDefault(vin) != convoyId)
        {
            return Task.FromResult(false);
        }

        vehicles[vin] = null;
        return Task.FromResult(true);
    }

    public Task<bool> PublishTruckListAsync(int convoyId, DateTime publishedAt, CancellationToken cancellationToken)
    {
        if (!convoys.TryGetValue(convoyId, out var convoy) || convoy.TruckListPublished)
        {
            return Task.FromResult(false);
        }

        convoys[convoyId] = convoy with { TruckListPublishedAt = publishedAt };
        return Task.FromResult(true);
    }
}
