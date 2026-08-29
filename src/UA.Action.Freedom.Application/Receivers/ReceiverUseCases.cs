using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Receivers;

/// <summary>Register a receiving organisation. The reference is minted here.</summary>
public sealed record CreateReceiverCommand(string Organisation, string Region);

public sealed class CreateReceiverHandler(IReceiverRepository repository)
    : ICommandHandler<CreateReceiverCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateReceiverCommand command, CancellationToken cancellationToken)
    {
        // Opaque by design: the reference travels on manifests and is joined on all over the
        // application, so it must say nothing about the receiver and must not be guessable.
        var receiverRef = Guid.NewGuid();

        await repository.AddAsync(
            new ReceiverReadModel(receiverRef, command.Organisation, command.Region), cancellationToken);

        return receiverRef;
    }
}

/// <summary>Change a receiver's organisation or region.</summary>
public sealed record UpdateReceiverCommand(Guid Ref, string Organisation, string Region);

public enum UpdateReceiverOutcome
{
    Updated,
    NotFound
}

public sealed class UpdateReceiverHandler(IReceiverRepository repository)
    : ICommandHandler<UpdateReceiverCommand, UpdateReceiverOutcome>
{
    public async Task<UpdateReceiverOutcome> HandleAsync(
        UpdateReceiverCommand command, CancellationToken cancellationToken)
    {
        var updated = await repository.UpdateAsync(
            new ReceiverReadModel(command.Ref, command.Organisation, command.Region), cancellationToken);

        return updated ? UpdateReceiverOutcome.Updated : UpdateReceiverOutcome.NotFound;
    }
}

/// <summary>Remove a receiver, and any delivery detail held for it.</summary>
public sealed record DeleteReceiverCommand(Guid Ref);

public enum DeleteReceiverOutcome
{
    Deleted,
    NotFound
}

public sealed class DeleteReceiverHandler(
    IReceiverRepository repository,
    IReceiverDetailRepository detail)
    : ICommandHandler<DeleteReceiverCommand, DeleteReceiverOutcome>
{
    public async Task<DeleteReceiverOutcome> HandleAsync(
        DeleteReceiverCommand command, CancellationToken cancellationToken)
    {
        // Detail first, and through the Ground Officer identity, because the application's own
        // identity cannot touch the sensitive schema — a plain DELETE on dbo.Receiver would be
        // refused by the foreign key and leave the address behind. Removing the reference while
        // keeping the address is the worst of both worlds: data still held, nothing pointing at
        // it to say whose it is (§4.4.5).
        await detail.DeleteAsync(command.Ref, cancellationToken);

        var deleted = await repository.DeleteAsync(command.Ref, cancellationToken);

        return deleted ? DeleteReceiverOutcome.Deleted : DeleteReceiverOutcome.NotFound;
    }
}

/// <summary>Fetch one receiver — reference, organisation, region. Never the address.</summary>
public sealed record GetReceiverByRefQuery(Guid Ref);

public sealed class GetReceiverByRefHandler(IReceiverRepository repository)
    : IQueryHandler<GetReceiverByRefQuery, ReceiverReadModel?>
{
    public Task<ReceiverReadModel?> HandleAsync(GetReceiverByRefQuery query, CancellationToken cancellationToken)
        => repository.GetByRefAsync(query.Ref, cancellationToken);
}

/// <summary>A page of receivers ordered by organisation. Page size is clamped to 1..200.</summary>
public sealed record ListReceiversQuery(int Page, int PageSize);

public sealed class ListReceiversHandler(IReceiverRepository repository)
    : IQueryHandler<ListReceiversQuery, IReadOnlyList<ReceiverReadModel>>
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    public Task<IReadOnlyList<ReceiverReadModel>> HandleAsync(
        ListReceiversQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > MaxPageSize ? DefaultPageSize : query.PageSize;

        return repository.ListAsync(page, pageSize, cancellationToken);
    }
}
