using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Application.Boxes;

/// <summary>
/// A Loader places a box in a bay — the physical, on-site act of shelving it so it can be
/// found again. Allowed at any point in the box's life, not gated on validation: a box may be
/// checked in and shelved before a Loader gets round to weighing it.
/// </summary>
public sealed record AssignBoxBayCommand(int BoxId, int BayId, Guid AssignedByPersonId);

public enum AssignBoxBayOutcome
{
    Assigned,
    BoxNotFound,
    BayNotFound,
    NoSuchAssigner,
    LocationMismatch
}

/// <summary>
/// A box may only be in one bay, within one location, at a time. <see cref="AssignBoxBayOutcome.LocationMismatch"/>
/// covers both the bay belonging to a different location than the box currently sits in, and
/// the box not yet having a location at all — either way, the bay is not somewhere this box is.
/// </summary>
public sealed class AssignBoxBayHandler(
    IBoxRepository boxes, IBayRepository bays, IPersonRepository people)
    : ICommandHandler<AssignBoxBayCommand, AssignBoxBayOutcome>
{
    public async Task<AssignBoxBayOutcome> HandleAsync(AssignBoxBayCommand command, CancellationToken cancellationToken)
    {
        var box = await boxes.GetByIdAsync(command.BoxId, cancellationToken);
        if (box is null)
        {
            return AssignBoxBayOutcome.BoxNotFound;
        }

        var bay = await bays.GetByIdAsync(command.BayId, cancellationToken);
        if (bay is null)
        {
            return AssignBoxBayOutcome.BayNotFound;
        }

        if (box.LocationId is null || box.LocationId != bay.LocationId)
        {
            return AssignBoxBayOutcome.LocationMismatch;
        }

        // The person placing the box has to be a volunteer on file, same reasoning as
        // ValidateBoxHandler: a signature naming somebody who does not exist is worse than none.
        if (!await people.ExistsAsync(command.AssignedByPersonId, cancellationToken))
        {
            return AssignBoxBayOutcome.NoSuchAssigner;
        }

        await boxes.AssignBayAsync(
            command.BoxId, command.BayId, command.AssignedByPersonId, DateTime.UtcNow, cancellationToken);

        return AssignBoxBayOutcome.Assigned;
    }
}

/// <summary>Take a box out of its bay without placing it in another.</summary>
public sealed record VacateBoxBayCommand(int BoxId);

public enum VacateBoxBayOutcome
{
    Vacated,
    BoxNotFound,
    NoActiveAssignment
}

public sealed class VacateBoxBayHandler(IBoxRepository boxes)
    : ICommandHandler<VacateBoxBayCommand, VacateBoxBayOutcome>
{
    public async Task<VacateBoxBayOutcome> HandleAsync(VacateBoxBayCommand command, CancellationToken cancellationToken)
    {
        if (await boxes.VacateActiveBayAssignmentAsync(command.BoxId, cancellationToken))
        {
            return VacateBoxBayOutcome.Vacated;
        }

        return await boxes.ExistsAsync(command.BoxId, cancellationToken)
            ? VacateBoxBayOutcome.NoActiveAssignment
            : VacateBoxBayOutcome.BoxNotFound;
    }
}

/// <summary>The bay a box currently occupies, or <c>null</c> if it is not in one.</summary>
public sealed record GetBoxBayQuery(int BoxId);

public sealed class GetBoxBayHandler(IBoxRepository repository)
    : IQueryHandler<GetBoxBayQuery, BoxBayAssignmentReadModel?>
{
    public Task<BoxBayAssignmentReadModel?> HandleAsync(GetBoxBayQuery query, CancellationToken cancellationToken)
        => repository.GetActiveBayAssignmentAsync(query.BoxId, cancellationToken);
}

/// <summary>Every bay a box has occupied, most recent first.</summary>
public sealed record GetBoxBayHistoryQuery(int BoxId);

public sealed class GetBoxBayHistoryHandler(IBoxRepository repository)
    : IQueryHandler<GetBoxBayHistoryQuery, IReadOnlyList<BoxBayAssignmentReadModel>>
{
    public Task<IReadOnlyList<BoxBayAssignmentReadModel>> HandleAsync(GetBoxBayHistoryQuery query, CancellationToken cancellationToken)
        => repository.ListBayAssignmentHistoryAsync(query.BoxId, cancellationToken);
}
