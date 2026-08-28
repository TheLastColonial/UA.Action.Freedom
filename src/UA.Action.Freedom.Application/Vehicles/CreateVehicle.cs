using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Vehicles;

/// <summary>Record a newly sourced vehicle. VIN is the key and must be unique.</summary>
public sealed record CreateVehicleCommand(
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

public enum CreateVehicleOutcome
{
    Created,
    Conflict
}

public sealed class CreateVehicleHandler(IVehicleRepository repository)
    : ICommandHandler<CreateVehicleCommand, CreateVehicleOutcome>
{
    public async Task<CreateVehicleOutcome> HandleAsync(CreateVehicleCommand command, CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(command.Vin, cancellationToken))
        {
            return CreateVehicleOutcome.Conflict;
        }

        await repository.AddAsync(
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

        return CreateVehicleOutcome.Created;
    }
}
