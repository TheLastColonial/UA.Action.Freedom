using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Vehicles;

/// <summary>Remove the vehicle with this VIN.</summary>
public sealed record DeleteVehicleCommand(string Vin);

public enum DeleteVehicleOutcome
{
    Deleted,
    NotFound,
    StillReferenced
}

public sealed class DeleteVehicleHandler(IVehicleRepository repository)
    : ICommandHandler<DeleteVehicleCommand, DeleteVehicleOutcome>
{
    public async Task<DeleteVehicleOutcome> HandleAsync(DeleteVehicleCommand command, CancellationToken cancellationToken)
    {
        return await repository.DeleteAsync(command.Vin, cancellationToken) switch
        {
            DeleteResult.Deleted => DeleteVehicleOutcome.Deleted,
            DeleteResult.StillReferenced => DeleteVehicleOutcome.StillReferenced,
            _ => DeleteVehicleOutcome.NotFound,
        };
    }
}
