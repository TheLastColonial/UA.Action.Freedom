using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Locations;

/// <summary>Add a bay to a location.</summary>
public sealed record CreateBayCommand(int LocationId, string Code);

public enum CreateBayOutcome
{
    Created,
    LocationNotFound,
    CodeConflict
}

/// <summary>The outcome of <see cref="CreateBayCommand"/>, and the new bay's id when created.</summary>
public sealed record CreateBayResult(CreateBayOutcome Outcome, int Id);

public sealed class CreateBayHandler(IBayRepository bays, ILocationRepository locations)
    : ICommandHandler<CreateBayCommand, CreateBayResult>
{
    public async Task<CreateBayResult> HandleAsync(CreateBayCommand command, CancellationToken cancellationToken)
    {
        if (!await locations.ExistsAsync(command.LocationId, cancellationToken))
        {
            return new CreateBayResult(CreateBayOutcome.LocationNotFound, 0);
        }

        // The code only has to be distinct within the location — a bay code is a shelf label,
        // and every location keeps its own.
        if (await bays.CodeExistsAsync(command.LocationId, command.Code, excludeId: null, cancellationToken))
        {
            return new CreateBayResult(CreateBayOutcome.CodeConflict, 0);
        }

        var id = await bays.AddAsync(new BayReadModel(Id: 0, command.LocationId, command.Code), cancellationToken);
        return new CreateBayResult(CreateBayOutcome.Created, id);
    }
}

/// <summary>Rename a bay within its location.</summary>
public sealed record UpdateBayCommand(int Id, int LocationId, string Code);

public enum UpdateBayOutcome
{
    Updated,
    NotFound,
    CodeConflict
}

public sealed class UpdateBayHandler(IBayRepository bays)
    : ICommandHandler<UpdateBayCommand, UpdateBayOutcome>
{
    public async Task<UpdateBayOutcome> HandleAsync(UpdateBayCommand command, CancellationToken cancellationToken)
    {
        if (await bays.CodeExistsAsync(command.LocationId, command.Code, command.Id, cancellationToken))
        {
            return UpdateBayOutcome.CodeConflict;
        }

        var updated = await bays.UpdateAsync(
            new BayReadModel(command.Id, command.LocationId, command.Code), cancellationToken);

        return updated ? UpdateBayOutcome.Updated : UpdateBayOutcome.NotFound;
    }
}

/// <summary>Remove a bay.</summary>
public sealed record DeleteBayCommand(int Id);

public enum DeleteBayOutcome
{
    Deleted,
    NotFound
}

public sealed class DeleteBayHandler(IBayRepository bays)
    : ICommandHandler<DeleteBayCommand, DeleteBayOutcome>
{
    public async Task<DeleteBayOutcome> HandleAsync(DeleteBayCommand command, CancellationToken cancellationToken)
    {
        var deleted = await bays.DeleteAsync(command.Id, cancellationToken);
        return deleted ? DeleteBayOutcome.Deleted : DeleteBayOutcome.NotFound;
    }
}

/// <summary>The bays belonging to a location, or <c>null</c> if there is no such location.</summary>
public sealed record ListBaysQuery(int LocationId);

public sealed class ListBaysHandler(IBayRepository bays, ILocationRepository locations)
    : IQueryHandler<ListBaysQuery, IReadOnlyList<BayReadModel>?>
{
    public async Task<IReadOnlyList<BayReadModel>?> HandleAsync(ListBaysQuery query, CancellationToken cancellationToken)
    {
        if (!await locations.ExistsAsync(query.LocationId, cancellationToken))
        {
            return null;
        }

        return await bays.ListByLocationAsync(query.LocationId, cancellationToken);
    }
}
