using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Vehicles;

/// <summary>Replace every mutable field of the vehicle identified by <see cref="Vin"/>.</summary>
public sealed record UpdateVehicleCommand(
    string Vin,
    string Plate,
    string? Brand,
    string? Model,
    string? Colour,
    TransmissionType Transmission,
    string? Notes,
    int? Mileage,
    bool Servicing,
    int Year,
    FuelType Fuel,
    int? ConvoyId,
    string? PurchaserName,
    DateTime? PurchaseDate,
    int WeightKg);

public enum UpdateVehicleOutcome
{
    Updated,
    NotFound
}

public sealed class UpdateVehicleHandler(IVehicleRepository repository)
    : ICommandHandler<UpdateVehicleCommand, UpdateVehicleOutcome>
{
    public async Task<UpdateVehicleOutcome> HandleAsync(UpdateVehicleCommand command, CancellationToken cancellationToken)
    {
        var updated = await repository.UpdateAsync(
            new VehicleReadModel(
                command.Vin,
                command.Plate,
                command.Brand,
                command.Model,
                command.Colour,
                command.Transmission,
                command.Notes,
                command.Mileage,
                command.Servicing,
                command.Year,
                command.Fuel,
                command.ConvoyId,
                command.PurchaserName,
                command.PurchaseDate,
                command.WeightKg),
            cancellationToken);

        return updated ? UpdateVehicleOutcome.Updated : UpdateVehicleOutcome.NotFound;
    }
}
