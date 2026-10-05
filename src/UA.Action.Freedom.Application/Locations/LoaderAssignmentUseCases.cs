using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Application.Locations;

/// <summary>Who manages a location, and who did before. Null when there is no such location.</summary>
public sealed record ListLoadersQuery(int LocationId);

public sealed class ListLoadersHandler(ILocationRepository locations, ILoaderAssignmentRepository loaders)
    : IQueryHandler<ListLoadersQuery, IReadOnlyList<LoaderAssignmentReadModel>?>
{
    public async Task<IReadOnlyList<LoaderAssignmentReadModel>?> HandleAsync(
        ListLoadersQuery query, CancellationToken cancellationToken) =>
        await locations.ExistsAsync(query.LocationId, cancellationToken)
            ? await loaders.HistoryAsync(query.LocationId, cancellationToken)
            : null;
}

/// <summary>Make a Loader the manager of a location (O31). An Administrator's act.</summary>
public sealed record AssignLoaderCommand(int LocationId, Guid PersonId);

public enum AssignLoaderOutcome
{
    Assigned,
    LocationNotFound,
    PersonNotFound,
    AlreadyAssigned
}

public sealed class AssignLoaderHandler(
    ILocationRepository locations, IPersonRepository people, ILoaderAssignmentRepository loaders)
    : ICommandHandler<AssignLoaderCommand, AssignLoaderOutcome>
{
    public async Task<AssignLoaderOutcome> HandleAsync(AssignLoaderCommand command, CancellationToken cancellationToken)
    {
        if (!await locations.ExistsAsync(command.LocationId, cancellationToken))
        {
            return AssignLoaderOutcome.LocationNotFound;
        }

        if (!await people.ExistsAsync(command.PersonId, cancellationToken))
        {
            return AssignLoaderOutcome.PersonNotFound;
        }

        return await loaders.AssignAsync(command.LocationId, command.PersonId, DateTime.UtcNow, cancellationToken) switch
        {
            AssignLoaderResult.Assigned => AssignLoaderOutcome.Assigned,
            _ => AssignLoaderOutcome.AlreadyAssigned,
        };
    }
}

/// <summary>End a Loader's management of a location. The row is kept, closed.</summary>
public sealed record UnassignLoaderCommand(int LocationId, Guid PersonId);

public enum UnassignLoaderOutcome
{
    Unassigned,
    NotAssigned
}

public sealed class UnassignLoaderHandler(ILoaderAssignmentRepository loaders)
    : ICommandHandler<UnassignLoaderCommand, UnassignLoaderOutcome>
{
    public async Task<UnassignLoaderOutcome> HandleAsync(
        UnassignLoaderCommand command, CancellationToken cancellationToken) =>
        await loaders.UnassignAsync(command.LocationId, command.PersonId, DateTime.UtcNow, cancellationToken)
            ? UnassignLoaderOutcome.Unassigned
            : UnassignLoaderOutcome.NotAssigned;
}
