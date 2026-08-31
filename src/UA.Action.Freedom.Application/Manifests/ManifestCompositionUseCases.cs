using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Application.Manifests;

/// <summary>Assign the driver team crewing one leg of the journey.</summary>
public sealed record SetManifestTeamCommand(
    string Id, ManifestLeg Leg, Guid PrimaryPersonId, Guid? SecondaryPersonId);

public enum SetManifestTeamOutcome
{
    Set,
    NotFound,
    Frozen,
    NoSuchDriver,
    DriverIsNotADriver,
    SameDriverTwice
}

public sealed class SetManifestTeamHandler(IManifestRepository repository, IPersonRepository people)
    : ICommandHandler<SetManifestTeamCommand, SetManifestTeamOutcome>
{
    public async Task<SetManifestTeamOutcome> HandleAsync(
        SetManifestTeamCommand command, CancellationToken cancellationToken)
    {
        var manifest = await repository.GetByIdAsync(command.Id, cancellationToken);

        if (manifest is null)
        {
            return SetManifestTeamOutcome.NotFound;
        }

        if (manifest.Frozen)
        {
            return SetManifestTeamOutcome.Frozen;
        }

        // A pair is two people. The same volunteer as primary and secondary would look crewed
        // while leaving somebody driving a leg to Ukraine alone.
        if (command.SecondaryPersonId == command.PrimaryPersonId)
        {
            return SetManifestTeamOutcome.SameDriverTwice;
        }

        foreach (var personId in new[] { command.PrimaryPersonId, command.SecondaryPersonId }.OfType<Guid>())
        {
            var person = await people.GetByIdAsync(personId, cancellationToken);

            if (person is null)
            {
                return SetManifestTeamOutcome.NoSuchDriver;
            }

            // Being on the volunteer roster is not the same as having volunteered to drive.
            if (!person.IsDriver)
            {
                return SetManifestTeamOutcome.DriverIsNotADriver;
            }
        }

        await repository.SetTeamAsync(
            command.Id,
            new ManifestDriverTeamReadModel(command.Leg, command.PrimaryPersonId, command.SecondaryPersonId),
            cancellationToken);

        return SetManifestTeamOutcome.Set;
    }
}

/// <summary>The driver teams on a manifest, or <c>null</c> if there is no such manifest.</summary>
public sealed record ListManifestTeamsQuery(string Id);

public sealed class ListManifestTeamsHandler(IManifestRepository repository)
    : IQueryHandler<ListManifestTeamsQuery, IReadOnlyList<ManifestDriverTeamReadModel>?>
{
    public async Task<IReadOnlyList<ManifestDriverTeamReadModel>?> HandleAsync(
        ListManifestTeamsQuery query, CancellationToken cancellationToken)
        => await repository.ExistsAsync(query.Id, cancellationToken)
            ? await repository.ListTeamsAsync(query.Id, cancellationToken)
            : null;
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
    /// <summary>Two drivers and their bags. A border-check estimate, deliberately fixed.</summary>
    private const int CrewAndBagsKg = 100 * 2;

    /// <summary>Fuel allowance. Also deliberately fixed.</summary>
    private const int FuelKg = 45;

    public async Task<ManifestWeightReadModel?> HandleAsync(
        GetManifestWeightQuery query, CancellationToken cancellationToken)
    {
        if (!await repository.ExistsAsync(query.Id, cancellationToken))
        {
            return null;
        }

        var vehicleKg = await repository.GetVehicleWeightKgAsync(query.Id, cancellationToken);
        var boxes = await repository.ListBoxesAsync(query.Id, cancellationToken);

        var cargoKg = boxes.Sum(box => box.WeightKg);

        return new ManifestWeightReadModel(
            vehicleKg,
            cargoKg,
            CrewAndBagsKg,
            FuelKg,
            vehicleKg + cargoKg + CrewAndBagsKg + FuelKg,
            // Unvalidated boxes weigh zero until a Loader says otherwise, so a total that
            // includes any of them is provisional and has to say so.
            boxes.Count(box => !box.Validated));
    }
}
