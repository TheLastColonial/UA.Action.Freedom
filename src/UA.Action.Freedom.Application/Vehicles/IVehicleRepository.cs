using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Vehicles;

/// <summary>
/// Persistence port for <see cref="VehicleReadModel"/>. Implemented in the Data project; the
/// handlers here depend only on this. The write methods report whether a row was affected so
/// handlers can distinguish "not found" from "done" without a prior read.
/// </summary>
public interface IVehicleRepository
{
    Task<VehicleReadModel?> GetByVinAsync(string vin, CancellationToken cancellationToken);

    Task<IReadOnlyList<VehicleReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string vin, CancellationToken cancellationToken);

    Task AddAsync(VehicleReadModel vehicle, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(VehicleReadModel vehicle, CancellationToken cancellationToken);

    /// <summary>Refused (<see cref="DeleteResult.StillReferenced"/>) while a manifest names the vehicle.</summary>
    Task<DeleteResult> DeleteAsync(string vin, CancellationToken cancellationToken);

    Task<bool> RecordInspectionAsync(
        string vin, InspectionStatus status, string? notes, CancellationToken cancellationToken);
}
