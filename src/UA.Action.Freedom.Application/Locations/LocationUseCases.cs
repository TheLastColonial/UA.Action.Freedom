using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Locations;

/// <summary>Record a new distribution hub.</summary>
public sealed record CreateLocationCommand(
    string Name, string? House, string? Street, string? City, string? Country, string? Postcode);

public sealed class CreateLocationHandler(ILocationRepository repository)
    : ICommandHandler<CreateLocationCommand, int>
{
    public Task<int> HandleAsync(CreateLocationCommand command, CancellationToken cancellationToken)
        => repository.AddAsync(
            new LocationReadModel(
                Id: 0,
                command.Name,
                command.House,
                command.Street,
                command.City,
                command.Country,
                command.Postcode),
            cancellationToken);
}

/// <summary>Change a distribution hub's name or address.</summary>
public sealed record UpdateLocationCommand(
    int Id, string Name, string? House, string? Street, string? City, string? Country, string? Postcode);

public enum UpdateLocationOutcome
{
    Updated,
    NotFound
}

public sealed class UpdateLocationHandler(ILocationRepository repository)
    : ICommandHandler<UpdateLocationCommand, UpdateLocationOutcome>
{
    public async Task<UpdateLocationOutcome> HandleAsync(UpdateLocationCommand command, CancellationToken cancellationToken)
    {
        var updated = await repository.UpdateAsync(
            new LocationReadModel(
                command.Id,
                command.Name,
                command.House,
                command.Street,
                command.City,
                command.Country,
                command.Postcode),
            cancellationToken);

        return updated ? UpdateLocationOutcome.Updated : UpdateLocationOutcome.NotFound;
    }
}

/// <summary>Remove a distribution hub.</summary>
public sealed record DeleteLocationCommand(int Id);

public enum DeleteLocationOutcome
{
    Deleted,
    NotFound
}

public sealed class DeleteLocationHandler(ILocationRepository repository)
    : ICommandHandler<DeleteLocationCommand, DeleteLocationOutcome>
{
    public async Task<DeleteLocationOutcome> HandleAsync(DeleteLocationCommand command, CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteAsync(command.Id, cancellationToken);
        return deleted ? DeleteLocationOutcome.Deleted : DeleteLocationOutcome.NotFound;
    }
}

/// <summary>Fetch one location, or <c>null</c> if there is no such location.</summary>
public sealed record GetLocationByIdQuery(int Id);

public sealed class GetLocationByIdHandler(ILocationRepository repository)
    : IQueryHandler<GetLocationByIdQuery, LocationReadModel?>
{
    public Task<LocationReadModel?> HandleAsync(GetLocationByIdQuery query, CancellationToken cancellationToken)
        => repository.GetByIdAsync(query.Id, cancellationToken);
}

/// <summary>A page of locations. Page size is clamped to 1..200.</summary>
public sealed record ListLocationsQuery(int Page, int PageSize);

public sealed class ListLocationsHandler(ILocationRepository repository)
    : IQueryHandler<ListLocationsQuery, IReadOnlyList<LocationReadModel>>
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    public Task<IReadOnlyList<LocationReadModel>> HandleAsync(ListLocationsQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > MaxPageSize ? DefaultPageSize : query.PageSize;

        return repository.ListAsync(page, pageSize, cancellationToken);
    }
}
