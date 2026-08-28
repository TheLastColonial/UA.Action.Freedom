using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Vehicles;

/// <summary>Fetch one vehicle by VIN, or <c>null</c> if there is no such vehicle.</summary>
public sealed record GetVehicleByVinQuery(string Vin);

public sealed class GetVehicleByVinHandler(IVehicleRepository repository)
    : IQueryHandler<GetVehicleByVinQuery, VehicleReadModel?>
{
    public Task<VehicleReadModel?> HandleAsync(GetVehicleByVinQuery query, CancellationToken cancellationToken)
        => repository.GetByVinAsync(query.Vin, cancellationToken);
}
