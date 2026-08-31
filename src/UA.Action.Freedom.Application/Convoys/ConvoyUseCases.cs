using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>Plan a convoy: when it leaves and when it is expected to arrive.</summary>
public sealed record CreateConvoyCommand(DateTime Start, DateTime ExpectedEnd);

/// <summary>
/// Creating a convoy cannot conflict, so this handler returns the identifier the database
/// assigned rather than an outcome enum.
/// </summary>
public sealed class CreateConvoyHandler(IConvoyRepository repository)
    : ICommandHandler<CreateConvoyCommand, int>
{
    public Task<int> HandleAsync(CreateConvoyCommand command, CancellationToken cancellationToken)
        => repository.AddAsync(command.Start, command.ExpectedEnd, cancellationToken);
}

/// <summary>Change a convoy's departure or expected arrival.</summary>
public sealed record UpdateConvoyCommand(int Id, DateTime Start, DateTime ExpectedEnd);

public enum UpdateConvoyOutcome
{
    Updated,
    NotFound
}

public sealed class UpdateConvoyHandler(IConvoyRepository repository)
    : ICommandHandler<UpdateConvoyCommand, UpdateConvoyOutcome>
{
    public async Task<UpdateConvoyOutcome> HandleAsync(UpdateConvoyCommand command, CancellationToken cancellationToken)
    {
        // TruckListPublishedAt is not settable here: publishing is its own transition, and an
        // update that could quietly stamp or clear it would route around that rule.
        var updated = await repository.UpdateAsync(
            new ConvoyReadModel(command.Id, command.Start, command.ExpectedEnd, TruckListPublishedAt: null),
            cancellationToken);

        return updated ? UpdateConvoyOutcome.Updated : UpdateConvoyOutcome.NotFound;
    }
}

/// <summary>Remove a convoy that is not going to run.</summary>
public sealed record DeleteConvoyCommand(int Id);

public enum DeleteConvoyOutcome
{
    Deleted,
    NotFound
}

public sealed class DeleteConvoyHandler(IConvoyRepository repository)
    : ICommandHandler<DeleteConvoyCommand, DeleteConvoyOutcome>
{
    public async Task<DeleteConvoyOutcome> HandleAsync(DeleteConvoyCommand command, CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteAsync(command.Id, cancellationToken);
        return deleted ? DeleteConvoyOutcome.Deleted : DeleteConvoyOutcome.NotFound;
    }
}

/// <summary>Fetch one convoy, or <c>null</c> if there is no such convoy.</summary>
public sealed record GetConvoyByIdQuery(int Id);

public sealed class GetConvoyByIdHandler(IConvoyRepository repository)
    : IQueryHandler<GetConvoyByIdQuery, ConvoyReadModel?>
{
    public Task<ConvoyReadModel?> HandleAsync(GetConvoyByIdQuery query, CancellationToken cancellationToken)
        => repository.GetByIdAsync(query.Id, cancellationToken);
}

/// <summary>A page of convoys, newest departure first. Page size is clamped to 1..200.</summary>
public sealed record ListConvoysQuery(int Page, int PageSize);

public sealed class ListConvoysHandler(IConvoyRepository repository)
    : IQueryHandler<ListConvoysQuery, IReadOnlyList<ConvoyReadModel>>
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    public Task<IReadOnlyList<ConvoyReadModel>> HandleAsync(ListConvoysQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > MaxPageSize ? DefaultPageSize : query.PageSize;

        return repository.ListAsync(page, pageSize, cancellationToken);
    }
}
