using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// A dictionary-backed <see cref="IConvoyRepository"/> so the endpoint tests run without a
/// database. The Dapper implementation is covered separately by the integration tests.
/// </summary>
/// <remarks>
/// Every rule here mirrors a statement in <c>ConvoyRepository</c>: drivers are keyed by VIN
/// alone (<c>PK_VehicleDriver</c>), and are cleared when the vehicle leaves its convoy or the
/// convoy is cancelled. A fake that is kinder than the SQL lets a test pass that production fails.
/// </remarks>
internal sealed class InMemoryConvoyRepository : IConvoyRepository
{
    private sealed record StoredVehicle(int? ConvoyId, InspectionStatus Inspection);

    private readonly Dictionary<int, ConvoyReadModel> convoys = [];
    private readonly Dictionary<int, List<RouteStopReadModel>> routes = [];

    /// <summary>Standing in for <c>dbo.Vehicle.ConvoyId</c> and <c>InspectionStatus</c>.</summary>
    private readonly Dictionary<string, StoredVehicle> vehicles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Standing in for <c>dbo.VehicleDriver</c>: VIN to the people crewing it.</summary>
    private readonly Dictionary<string, List<Guid>> vehicleDrivers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Names for the crew list, standing in for the join to <c>dbo.Person</c>.</summary>
    private readonly Dictionary<Guid, VehicleDriverReadModel> persons = [];

    private int nextId = 1;

    public InMemoryConvoyRepository(params ConvoyReadModel[] seed)
    {
        foreach (var convoy in seed)
        {
            convoys[convoy.Id] = convoy;
            nextId = Math.Max(nextId, convoy.Id + 1);
        }
    }

    /// <summary>
    /// Adds a vehicle. It has passed its inspection unless told otherwise, because that is the
    /// only kind a convoy will take and most tests are not about the inspection.
    /// </summary>
    public InMemoryConvoyRepository WithVehicle(
        string vin, int? onConvoy = null, InspectionStatus inspection = InspectionStatus.Passed)
    {
        vehicles[vin] = new StoredVehicle(onConvoy, inspection);
        return this;
    }

    public InMemoryConvoyRepository WithPerson(Guid personId, string firstName, string lastName)
    {
        persons[personId] = new VehicleDriverReadModel(personId, firstName, lastName);
        return this;
    }

    public InMemoryConvoyRepository WithDriver(string vin, Guid personId)
    {
        DriversOf(vin).Add(personId);
        return this;
    }

    public int Count => convoys.Count;

    public int? ConvoyOf(string vin) => vehicles.GetValueOrDefault(vin)?.ConvoyId;

    public IReadOnlyList<Guid> DriverIdsOf(string vin) => vehicleDrivers.GetValueOrDefault(vin, []);

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

        foreach (var vin in VinsOn(id))
        {
            Release(vin);
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
            VinsOn(convoyId)
                .Order(StringComparer.Ordinal)
                .Select(vin => new ConvoyVehicleReadModel(vin, "AB12CDE", 1_400, DriverIdsOf(vin).Count))
                .ToList());

    public Task<AssignVehicleResult> AssignVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        if (!vehicles.TryGetValue(vin, out var vehicle))
        {
            return Task.FromResult(AssignVehicleResult.VehicleNotFound);
        }

        if (vehicle.Inspection != InspectionStatus.Passed)
        {
            return Task.FromResult(AssignVehicleResult.NotPassedInspection);
        }

        if (vehicle.ConvoyId is not null && vehicle.ConvoyId != convoyId)
        {
            return Task.FromResult(AssignVehicleResult.OnAnotherConvoy);
        }

        vehicles[vin] = vehicle with { ConvoyId = convoyId };
        return Task.FromResult(AssignVehicleResult.Assigned);
    }

    public Task<bool> UnassignVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        if (!IsOn(convoyId, vin))
        {
            return Task.FromResult(false);
        }

        Release(vin);
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

    public Task<IReadOnlyList<VehicleDriverReadModel>?> ListVehicleDriversAsync(
        int convoyId, string vin, CancellationToken cancellationToken)
    {
        if (!IsOn(convoyId, vin))
        {
            return Task.FromResult<IReadOnlyList<VehicleDriverReadModel>?>(null);
        }

        var drivers = DriverIdsOf(vin)
            .Select(personId => persons[personId])
            .OrderBy(driver => driver.LastName)
            .ThenBy(driver => driver.FirstName)
            .ToList();

        return Task.FromResult<IReadOnlyList<VehicleDriverReadModel>?>(drivers);
    }

    public Task<AssignDriverResult> AssignDriverAsync(
        int convoyId, string vin, Guid personId, CancellationToken cancellationToken)
    {
        if (!IsOn(convoyId, vin))
        {
            return Task.FromResult(AssignDriverResult.VehicleNotOnConvoy);
        }

        var drivers = DriversOf(vin);
        if (drivers.Contains(personId))
        {
            return Task.FromResult(AssignDriverResult.AlreadyAssigned);
        }

        drivers.Add(personId);
        return Task.FromResult(AssignDriverResult.Assigned);
    }

    public Task<bool> UnassignDriverAsync(int convoyId, string vin, Guid personId, CancellationToken cancellationToken) =>
        Task.FromResult(IsOn(convoyId, vin) && DriversOf(vin).Remove(personId));

    private bool IsOn(int convoyId, string vin) => ConvoyOf(vin) == convoyId;

    private List<string> VinsOn(int convoyId) =>
        vehicles.Where(entry => entry.Value.ConvoyId == convoyId).Select(entry => entry.Key).ToList();

    private List<Guid> DriversOf(string vin)
    {
        if (!vehicleDrivers.TryGetValue(vin, out var drivers))
        {
            drivers = [];
            vehicleDrivers[vin] = drivers;
        }

        return drivers;
    }

    private void Release(string vin)
    {
        vehicles[vin] = vehicles[vin] with { ConvoyId = null };
        vehicleDrivers.Remove(vin);
    }
}
