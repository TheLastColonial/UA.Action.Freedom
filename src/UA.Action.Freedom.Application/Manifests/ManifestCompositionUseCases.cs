using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Manifests;

/// <summary>
/// The crew travelling with this manifest's vehicle, per leg. <c>null</c> if there is no such
/// manifest.
/// </summary>
/// <remarks>
/// A read, not a write. The manifest used to own its own driver teams, assigned through
/// <c>PUT /manifests/{id}/teams/{leg}</c> and connected to the convoy's crew by nothing at all —
/// so the printed document could name people who were not in the vehicle, while the insurance that
/// actually gates departure covered somebody else. Crewing is now one act, on the truck-list entry
/// (<c>PUT /convoys/{id}/vehicles/{vin}/crew/{personId}</c>), and the manifest reports it.
/// </remarks>
public sealed record ListManifestCrewQuery(string Id);

public sealed class ListManifestCrewHandler(IManifestRepository repository, IConvoyVehicleRepository truckList)
    : IQueryHandler<ListManifestCrewQuery, IReadOnlyList<VehicleCrewReadModel>?>
{
    public async Task<IReadOnlyList<VehicleCrewReadModel>?> HandleAsync(
        ListManifestCrewQuery query, CancellationToken cancellationToken)
    {
        var manifest = await repository.GetByIdAsync(query.Id, cancellationToken);

        return manifest is null
            ? null
            : await truckList.ListCrewAsync(manifest.ConvoyId, manifest.Vin, leg: null, cancellationToken);
    }
}

/// <summary>Put a box on the manifest.</summary>
public sealed record AddManifestBoxCommand(string Id, int BoxId);

public enum ManifestBoxOutcome
{
    Changed,
    ManifestNotFound,
    BoxNotFound,
    Frozen
}

public sealed class AddManifestBoxHandler(IManifestRepository repository)
    : ICommandHandler<AddManifestBoxCommand, ManifestBoxOutcome>
{
    public async Task<ManifestBoxOutcome> HandleAsync(
        AddManifestBoxCommand command, CancellationToken cancellationToken)
    {
        var manifest = await repository.GetByIdAsync(command.Id, cancellationToken);

        if (manifest is null)
        {
            return ManifestBoxOutcome.ManifestNotFound;
        }

        // Cargo is what the GMR describes, so it is the last thing that may change afterwards.
        if (manifest.Frozen)
        {
            return ManifestBoxOutcome.Frozen;
        }

        return await repository.AddBoxAsync(command.Id, command.BoxId, cancellationToken)
            ? ManifestBoxOutcome.Changed
            : ManifestBoxOutcome.BoxNotFound;
    }
}

/// <summary>Take a box off the manifest.</summary>
public sealed record RemoveManifestBoxCommand(string Id, int BoxId);

public sealed class RemoveManifestBoxHandler(IManifestRepository repository)
    : ICommandHandler<RemoveManifestBoxCommand, ManifestBoxOutcome>
{
    public async Task<ManifestBoxOutcome> HandleAsync(
        RemoveManifestBoxCommand command, CancellationToken cancellationToken)
    {
        var manifest = await repository.GetByIdAsync(command.Id, cancellationToken);

        if (manifest is null)
        {
            return ManifestBoxOutcome.ManifestNotFound;
        }

        if (manifest.Frozen)
        {
            return ManifestBoxOutcome.Frozen;
        }

        return await repository.RemoveBoxAsync(command.Id, command.BoxId, cancellationToken)
            ? ManifestBoxOutcome.Changed
            : ManifestBoxOutcome.BoxNotFound;
    }
}

/// <summary>The cargo on a manifest, or <c>null</c> if there is no such manifest.</summary>
public sealed record ListManifestBoxesQuery(string Id);

public sealed class ListManifestBoxesHandler(IManifestRepository repository)
    : IQueryHandler<ListManifestBoxesQuery, IReadOnlyList<ManifestBoxReadModel>?>
{
    public async Task<IReadOnlyList<ManifestBoxReadModel>?> HandleAsync(
        ListManifestBoxesQuery query, CancellationToken cancellationToken)
        => await repository.ExistsAsync(query.Id, cancellationToken)
            ? await repository.ListBoxesAsync(query.Id, cancellationToken)
            : null;
}

/// <summary>The total weight for a border check, or <c>null</c> if there is no such manifest.</summary>
public sealed record GetManifestWeightQuery(string Id);

public sealed class GetManifestWeightHandler(IManifestRepository repository)
    : IQueryHandler<GetManifestWeightQuery, ManifestWeightReadModel?>
{
    public async Task<ManifestWeightReadModel?> HandleAsync(
        GetManifestWeightQuery query, CancellationToken cancellationToken)
    {
        if (!await repository.ExistsAsync(query.Id, cancellationToken))
        {
            return null;
        }

        var vehicleKg = await repository.GetVehicleWeightKgAsync(query.Id, cancellationToken);
        var boxes = await repository.ListBoxesAsync(query.Id, cancellationToken);
        var capacity = await repository.GetVehicleCargoCapacityAsync(query.Id, cancellationToken);

        var cargoKg = boxes.Sum(box => box.WeightKg);

        // Advisory only: this never rejects anything, and either side missing data just means
        // nothing can be flagged for that box (docs/domain/key-concepts.md § Manifest weight).
        var cargoOverweight = capacity.MaxCargoWeightKg is { } maxCargoWeightKg && cargoKg > maxCargoWeightKg;
        var oversizedBoxIds = boxes
            .Where(box => !FitsCargoSpace(box, capacity))
            .Select(box => box.BoxId)
            .ToList();

        return new ManifestWeightReadModel(
            vehicleKg,
            cargoKg,
            ManifestWeight.CrewAndBagsKg,
            ManifestWeight.FuelKg,
            ManifestWeight.Total(vehicleKg, cargoKg),
            // Unvalidated boxes weigh zero until a Loader says otherwise, so a total that
            // includes any of them is provisional and has to say so.
            boxes.Count(box => !box.Validated),
            capacity.MaxCargoWeightKg,
            cargoOverweight,
            oversizedBoxIds);
    }

    /// <summary>
    /// Whether <paramref name="box"/> fits within the vehicle's cargo space, ignoring which way
    /// round it is packed. Fits (i.e. cannot be flagged) whenever either side has no recorded
    /// dimensions — a box or vehicle nobody has measured cannot be judged oversized. This is a
    /// heuristic, not a guarantee of a physical fit: comparing the two sets of dimensions sorted
    /// largest-to-smallest allows for any rotation without doing full 3-D packing.
    /// </summary>
    private static bool FitsCargoSpace(ManifestBoxReadModel box, VehicleCargoCapacityReadModel capacity)
    {
        if (box.WidthCm is not { } boxWidth || box.DepthCm is not { } boxDepth || box.HeightCm is not { } boxHeight)
        {
            return true;
        }

        if (capacity.CargoWidthCm is not { } cargoWidth
            || capacity.CargoDepthCm is not { } cargoDepth
            || capacity.CargoHeightCm is not { } cargoHeight)
        {
            return true;
        }

        var boxDimensions = new[] { boxWidth, boxDepth, boxHeight }.OrderDescending().ToArray();
        var cargoDimensions = new[] { cargoWidth, cargoDepth, cargoHeight }.OrderDescending().ToArray();

        return boxDimensions[0] <= cargoDimensions[0]
            && boxDimensions[1] <= cargoDimensions[1]
            && boxDimensions[2] <= cargoDimensions[2];
    }
}
