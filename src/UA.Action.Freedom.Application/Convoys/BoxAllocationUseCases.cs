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
    BoxVoided,
    Frozen
}

/// <remarks>
/// Transitional: while the manifest still carries the GMR freeze, a vehicle whose manifest is
/// frozen neither gains nor loses cargo — and moving a box <em>off</em> a frozen vehicle is a
/// change to its load just as much as moving one on. Plan 15 removes the freeze.
/// </remarks>
public sealed class AllocateBoxHandler(IConvoyVehicleRepository truckList, IManifestRepository manifests)
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

        if (await manifests.IsLoadFrozenAsync(command.ConvoyId, command.Vin, cancellationToken))
        {
            return AllocateBoxOutcome.Frozen;
        }

        var current = await truckList.GetBoxAllocationAsync(command.BoxId, cancellationToken);

        if (current is not null
            && await manifests.IsLoadFrozenAsync(current.ConvoyId.Value, current.Vin, cancellationToken))
        {
            return AllocateBoxOutcome.Frozen;
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
    NotAllocated,
    Frozen
}

public sealed class RemoveBoxAllocationHandler(IConvoyVehicleRepository truckList, IManifestRepository manifests)
    : ICommandHandler<RemoveBoxAllocationCommand, RemoveBoxAllocationOutcome>
{
    public async Task<RemoveBoxAllocationOutcome> HandleAsync(
        RemoveBoxAllocationCommand command, CancellationToken cancellationToken)
    {
        if (await truckList.GetAsync(command.ConvoyId, command.Vin, cancellationToken) is null)
        {
            return RemoveBoxAllocationOutcome.VehicleNotOnConvoy;
        }

        if (await manifests.IsLoadFrozenAsync(command.ConvoyId, command.Vin, cancellationToken))
        {
            return RemoveBoxAllocationOutcome.Frozen;
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
