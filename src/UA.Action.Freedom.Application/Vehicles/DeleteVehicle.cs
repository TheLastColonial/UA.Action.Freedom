using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Vehicles;

/// <summary>Remove the vehicle with this VIN.</summary>
public sealed record DeleteVehicleCommand(string Vin);

public enum DeleteVehicleOutcome
{
    Deleted,
    NotFound
}

public sealed class DeleteVehicleHandler(IVehicleRepository repository)
    : ICommandHandler<DeleteVehicleCommand, DeleteVehicleOutcome>
{
    public async Task<DeleteVehicleOutcome> HandleAsync(DeleteVehicleCommand command, CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteAsync(command.Vin, cancellationToken);
        return deleted ? DeleteVehicleOutcome.Deleted : DeleteVehicleOutcome.NotFound;
    }
}
