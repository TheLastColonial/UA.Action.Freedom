using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>
/// Put a volunteer on a vehicle's crew for one leg of the journey. A driver must be registered to
/// drive; a passenger can be any volunteer.
/// </summary>
/// <remarks>
/// This is the only place crew is assigned. A manifest used to carry its own primary/secondary
/// driver teams, set through a different endpoint with a different rule, connected to this by
/// nothing at all — so the printed document could name a crew the insurance had never heard of.
/// </remarks>
public sealed record AssignCrewToVehicleCommand(
    int ConvoyId, string Vin, Guid PersonId, JourneyLeg Leg, CrewRole Role = CrewRole.Driver);

public enum AssignCrewOutcome
{
    Assigned,
    ConvoyNotFound,
    VehicleNotFound,
    PersonNotFound,
    PersonNotADriver,
    AlreadyAssigned,
    OnAnotherVehicle,
    ConvoyArrived,
    VehicleWithdrawn
}

public sealed class AssignCrewToVehicleHandler(
    IConvoyRepository convoys, IConvoyVehicleRepository truckList, IPersonRepository people)
    : ICommandHandler<AssignCrewToVehicleCommand, AssignCrewOutcome>
{
    public async Task<AssignCrewOutcome> HandleAsync(
        AssignCrewToVehicleCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.ConvoyId, cancellationToken);
        if (convoy is null)
        {
            return AssignCrewOutcome.ConvoyNotFound;
        }

        // After arrival the crew is history: the policy it was insured under has run its course.
        if (convoy.Arrived)
        {
            return AssignCrewOutcome.ConvoyArrived;
        }

        var entry = await truckList.GetAsync(command.ConvoyId, command.Vin, cancellationToken);
        if (entry is null)
        {
            return AssignCrewOutcome.VehicleNotFound;
        }

        // A vehicle that broke down and left is not going to be crewed for the rest of the journey.
        if (entry.Withdrawn)
        {
            return AssignCrewOutcome.VehicleWithdrawn;
        }

        var person = await people.GetByIdAsync(command.PersonId, cancellationToken);
        if (person is null)
        {
            return AssignCrewOutcome.PersonNotFound;
        }

        // Being on the volunteer roster is not the same as having volunteered to drive.
        if (command.Role == CrewRole.Driver && !person.IsDriver)
        {
            return AssignCrewOutcome.PersonNotADriver;
        }

        return await truckList.AssignCrewAsync(
            command.ConvoyId, command.Vin, command.PersonId, command.Leg, command.Role, cancellationToken) switch
        {
            AssignCrewResult.Assigned => AssignCrewOutcome.Assigned,
            AssignCrewResult.AlreadyAssigned => AssignCrewOutcome.AlreadyAssigned,
            AssignCrewResult.OnAnotherVehicle => AssignCrewOutcome.OnAnotherVehicle,
            _ => AssignCrewOutcome.VehicleNotFound,
        };
    }
}

/// <summary>Take a volunteer off a vehicle's crew for one leg.</summary>
public sealed record UnassignCrewFromVehicleCommand(int ConvoyId, string Vin, Guid PersonId, JourneyLeg Leg);

public enum UnassignCrewOutcome
{
    Unassigned,
    ConvoyNotFound,
    NotOnThisConvoy,
    NotAssigned,
    ConvoyArrived
}

public sealed class UnassignCrewFromVehicleHandler(IConvoyRepository convoys, IConvoyVehicleRepository truckList)
    : ICommandHandler<UnassignCrewFromVehicleCommand, UnassignCrewOutcome>
{
    public async Task<UnassignCrewOutcome> HandleAsync(
        UnassignCrewFromVehicleCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.ConvoyId, cancellationToken);
        if (convoy is null)
        {
            return UnassignCrewOutcome.ConvoyNotFound;
        }

        if (convoy.Arrived)
        {
            return UnassignCrewOutcome.ConvoyArrived;
        }

        if (await truckList.GetAsync(command.ConvoyId, command.Vin, cancellationToken) is null)
        {
            return UnassignCrewOutcome.NotOnThisConvoy;
        }

        return await truckList.UnassignCrewAsync(
            command.ConvoyId, command.Vin, command.PersonId, command.Leg, cancellationToken)
            ? UnassignCrewOutcome.Unassigned
            : UnassignCrewOutcome.NotAssigned;
    }
}

/// <summary>
/// The crew of a vehicle on a convoy, optionally for one leg only. Null when that vehicle is not
/// on this convoy.
/// </summary>
public sealed record ListVehicleCrewQuery(int ConvoyId, string Vin, JourneyLeg? Leg = null);

public sealed class ListVehicleCrewHandler(IConvoyVehicleRepository truckList)
    : IQueryHandler<ListVehicleCrewQuery, IReadOnlyList<VehicleCrewReadModel>?>
{
    public Task<IReadOnlyList<VehicleCrewReadModel>?> HandleAsync(
        ListVehicleCrewQuery query, CancellationToken cancellationToken) =>
        truckList.ListCrewAsync(query.ConvoyId, query.Vin, query.Leg, cancellationToken);
}
