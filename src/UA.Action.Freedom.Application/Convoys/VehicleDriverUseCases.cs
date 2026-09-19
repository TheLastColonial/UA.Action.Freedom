using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>Assign a driver to a vehicle on a convoy.</summary>
public sealed record AssignDriverToVehicleCommand(int ConvoyId, string Vin, Guid PersonId);

public enum AssignDriverOutcome
{
    Assigned,
    ConvoyNotFound,
    VehicleNotFound,
    PersonNotFound,
    PersonNotADriver,
    AlreadyAssigned
}

public sealed class AssignDriverToVehicleHandler(IConvoyRepository convoyRepository, IPersonRepository personRepository)
    : ICommandHandler<AssignDriverToVehicleCommand, AssignDriverOutcome>
{
    public async Task<AssignDriverOutcome> HandleAsync(
        AssignDriverToVehicleCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoyRepository.GetByIdAsync(command.ConvoyId, cancellationToken);
        if (convoy is null)
        {
            return AssignDriverOutcome.ConvoyNotFound;
        }

        var person = await personRepository.GetByIdAsync(command.PersonId, cancellationToken);
        if (person is null)
        {
            return AssignDriverOutcome.PersonNotFound;
        }

        if (!person.IsDriver)
        {
            return AssignDriverOutcome.PersonNotADriver;
        }

        return await convoyRepository.AssignDriverAsync(command.ConvoyId, command.Vin, command.PersonId, cancellationToken) switch
        {
            AssignDriverResult.Assigned => AssignDriverOutcome.Assigned,
            AssignDriverResult.AlreadyAssigned => AssignDriverOutcome.AlreadyAssigned,
            _ => AssignDriverOutcome.VehicleNotFound,
        };
    }
}

/// <summary>Unassign a driver from a vehicle on a convoy.</summary>
public sealed record UnassignDriverFromVehicleCommand(int ConvoyId, string Vin, Guid PersonId);

public enum UnassignDriverOutcome
{
    Unassigned,
    ConvoyNotFound,
    NotOnThisConvoy,
    NotAssigned
}

public sealed class UnassignDriverFromVehicleHandler(IConvoyRepository repository)
    : ICommandHandler<UnassignDriverFromVehicleCommand, UnassignDriverOutcome>
{
    public async Task<UnassignDriverOutcome> HandleAsync(
        UnassignDriverFromVehicleCommand command, CancellationToken cancellationToken)
    {
        var convoy = await repository.GetByIdAsync(command.ConvoyId, cancellationToken);
        if (convoy is null)
        {
            return UnassignDriverOutcome.ConvoyNotFound;
        }

        var drivers = await repository.ListVehicleDriversAsync(command.ConvoyId, command.Vin, cancellationToken);
        if (drivers is null)
        {
            return UnassignDriverOutcome.NotOnThisConvoy;
        }

        return await repository.UnassignDriverAsync(command.ConvoyId, command.Vin, command.PersonId, cancellationToken)
            ? UnassignDriverOutcome.Unassigned
            : UnassignDriverOutcome.NotAssigned;
    }
}

/// <summary>Get the drivers assigned to a vehicle on a convoy.</summary>
public sealed record ListVehicleDriversQuery(int ConvoyId, string Vin);

public sealed class ListVehicleDriversHandler(IConvoyRepository repository)
    : IQueryHandler<ListVehicleDriversQuery, IReadOnlyList<VehicleDriverReadModel>?>
{
    public async Task<IReadOnlyList<VehicleDriverReadModel>?> HandleAsync(
        ListVehicleDriversQuery query, CancellationToken cancellationToken)
    {
        return await repository.ListVehicleDriversAsync(query.ConvoyId, query.Vin, cancellationToken);
    }
}
