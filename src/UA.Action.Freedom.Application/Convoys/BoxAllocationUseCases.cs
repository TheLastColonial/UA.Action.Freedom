using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Manifests;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>Put a box on a vehicle's truck-list entry, moving it if it was on another.</summary>
public sealed record AllocateBoxCommand(int ConvoyId, string Vin, int BoxId);

public enum AllocateBoxOutcome
{
    Allocated,
    Moved,
    AlreadyAllocated,
    VehicleNotOnConvoy,
    VehicleWithdrawn,
    BoxNotFound,
    BoxVoided
}

/// <remarks>
/// A change to a vehicle's load is allowed whatever its sign-off says: <see cref="IConvoyVehicleRepository.AllocateBoxAsync"/>
/// sends an approved manifest, on the vehicle the box leaves as well as on the one it joins, back to Proposed in the same
/// transaction (X3).
/// </remarks>
public sealed class AllocateBoxHandler(IConvoyVehicleRepository truckList)
    : ICommandHandler<AllocateBoxCommand, AllocateBoxOutcome>
{
    public async Task<AllocateBoxOutcome> HandleAsync(AllocateBoxCommand command, CancellationToken cancellationToken)
    {
        var entry = await truckList.GetAsync(command.ConvoyId, command.Vin, cancellationToken);

        if (entry is null)
        {
            return AllocateBoxOutcome.VehicleNotOnConvoy;
        }

        if (entry.Withdrawn)
        {
            return AllocateBoxOutcome.VehicleWithdrawn;
        }

        return await truckList.AllocateBoxAsync(command.ConvoyId, command.Vin, command.BoxId, cancellationToken) switch
        {
            AllocateBoxResult.Allocated => AllocateBoxOutcome.Allocated,
            AllocateBoxResult.Moved => AllocateBoxOutcome.Moved,
            AllocateBoxResult.AlreadyAllocated => AllocateBoxOutcome.AlreadyAllocated,
            AllocateBoxResult.VehicleWithdrawn => AllocateBoxOutcome.VehicleWithdrawn,
            AllocateBoxResult.BoxNotFound => AllocateBoxOutcome.BoxNotFound,
            AllocateBoxResult.BoxVoided => AllocateBoxOutcome.BoxVoided,
            _ => AllocateBoxOutcome.VehicleNotOnConvoy,
        };
    }
}

/// <summary>Take a box off a vehicle's truck-list entry.</summary>
public sealed record RemoveBoxAllocationCommand(int ConvoyId, string Vin, int BoxId);

public enum RemoveBoxAllocationOutcome
{
    Removed,
    VehicleNotOnConvoy,
    NotAllocated
}

public sealed class RemoveBoxAllocationHandler(IConvoyVehicleRepository truckList)
    : ICommandHandler<RemoveBoxAllocationCommand, RemoveBoxAllocationOutcome>
{
    public async Task<RemoveBoxAllocationOutcome> HandleAsync(
        RemoveBoxAllocationCommand command, CancellationToken cancellationToken)
    {
        if (await truckList.GetAsync(command.ConvoyId, command.Vin, cancellationToken) is null)
        {
            return RemoveBoxAllocationOutcome.VehicleNotOnConvoy;
        }

        return await truckList.RemoveBoxAsync(command.ConvoyId, command.Vin, command.BoxId, cancellationToken)
            ? RemoveBoxAllocationOutcome.Removed
            : RemoveBoxAllocationOutcome.NotAllocated;
    }
}

/// <summary>The cargo on a vehicle, or null when it is not on this convoy.</summary>
public sealed record ListVehicleBoxesQuery(int ConvoyId, string Vin);

public sealed class ListVehicleBoxesHandler(IConvoyVehicleRepository truckList)
    : IQueryHandler<ListVehicleBoxesQuery, IReadOnlyList<ManifestBoxReadModel>?>
{
    public Task<IReadOnlyList<ManifestBoxReadModel>?> HandleAsync(
        ListVehicleBoxesQuery query, CancellationToken cancellationToken) =>
        truckList.ListBoxesAsync(query.ConvoyId, query.Vin, cancellationToken);
}
