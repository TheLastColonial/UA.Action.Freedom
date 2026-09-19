using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Vehicles;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// A dictionary-backed <see cref="IVehicleRepository"/> so the endpoint tests run without a
/// database. The Dapper implementation is covered separately by the integration tests.
/// </summary>
internal sealed class InMemoryVehicleRepository : IVehicleRepository
{
    private readonly Dictionary<string, VehicleReadModel> store = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryVehicleRepository(params VehicleReadModel[] seed)
    {
        foreach (var vehicle in seed)
        {
            store[vehicle.Vin] = vehicle;
        }
    }

    public int Count => store.Count;

    public bool Contains(string vin) => store.ContainsKey(vin);

    public Task<VehicleReadModel?> GetByVinAsync(string vin, CancellationToken cancellationToken) =>
        Task.FromResult(store.GetValueOrDefault(vin));

    public Task<IReadOnlyList<VehicleReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VehicleReadModel>>(
            store.Values.OrderBy(v => v.Vin, StringComparer.Ordinal).Skip((page - 1) * pageSize).Take(pageSize).ToList());

    public Task<bool> ExistsAsync(string vin, CancellationToken cancellationToken) =>
        Task.FromResult(store.ContainsKey(vin));

    public Task AddAsync(VehicleReadModel vehicle, CancellationToken cancellationToken)
    {
        store[vehicle.Vin] = vehicle;
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(VehicleReadModel vehicle, CancellationToken cancellationToken)
    {
        if (!store.TryGetValue(vehicle.Vin, out var existing))
        {
            return Task.FromResult(false);
        }

        store[vehicle.Vin] = vehicle with
        {
            ConvoyId = existing.ConvoyId,
            InspectionStatus = existing.InspectionStatus,
            InspectionNotes = existing.InspectionNotes,
        };
        return Task.FromResult(true);
    }

    /// <summary>Vehicles a manifest names, standing in for FK_Manifest_Vehicle.</summary>
    private readonly HashSet<string> onManifests = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryVehicleRepository NamedOnAManifest(string vin)
    {
        onManifests.Add(vin);
        return this;
    }

    public Task<DeleteResult> DeleteAsync(string vin, CancellationToken cancellationToken)
    {
        if (onManifests.Contains(vin) && store.ContainsKey(vin))
        {
            return Task.FromResult(DeleteResult.StillReferenced);
        }

        return Task.FromResult(store.Remove(vin) ? DeleteResult.Deleted : DeleteResult.NotFound);
    }

    public Task<bool> RecordInspectionAsync(
        string vin, InspectionStatus status, string? notes, CancellationToken cancellationToken)
    {
        if (!store.TryGetValue(vin, out var existing))
        {
            return Task.FromResult(false);
        }

        store[vin] = existing with { InspectionStatus = status, InspectionNotes = notes };
        return Task.FromResult(true);
    }
}
