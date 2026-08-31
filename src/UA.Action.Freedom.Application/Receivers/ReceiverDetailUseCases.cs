using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Receivers;

/// <summary>
/// Resolve a receiver's full delivery detail. Ground Officer only, and audited.
/// </summary>
/// <param name="PrincipalId">Who is asking — taken from the caller's token, never from the body.</param>
/// <param name="Reason">Why, if they gave one. Free text, recorded verbatim.</param>
public sealed record GetReceiverDetailQuery(Guid Ref, string PrincipalId, string? Reason);

public sealed class GetReceiverDetailHandler(IReceiverDetailRepository repository)
    : IQueryHandler<GetReceiverDetailQuery, ReceiverDetailReadModel?>
{
    public Task<ReceiverDetailReadModel?> HandleAsync(
        GetReceiverDetailQuery query, CancellationToken cancellationToken)
        => repository.ResolveAsync(query.Ref, query.PrincipalId, query.Reason, cancellationToken);
}

/// <summary>Record or replace the delivery detail for a receiver.</summary>
public sealed record SetReceiverDetailCommand(
    Guid Ref,
    string ContactName,
    string ContactPhone,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? PostCode,
    DateTime? DeleteAfter);

public enum SetReceiverDetailOutcome
{
    Set,
    ReceiverNotFound
}

public sealed class SetReceiverDetailHandler(
    IReceiverRepository receivers,
    IReceiverDetailRepository detail)
    : ICommandHandler<SetReceiverDetailCommand, SetReceiverDetailOutcome>
{
    public async Task<SetReceiverDetailOutcome> HandleAsync(
        SetReceiverDetailCommand command, CancellationToken cancellationToken)
    {
        // The receiver has to exist first. The detail table's foreign key would refuse the
        // insert anyway, but a 404 is a better answer than a database error, and checking here
        // keeps the failure on the non-sensitive side of the boundary.
        if (!await receivers.ExistsAsync(command.Ref, cancellationToken))
        {
            return SetReceiverDetailOutcome.ReceiverNotFound;
        }

        await detail.UpsertAsync(
            new ReceiverDetailReadModel(
                command.Ref,
                command.ContactName,
                command.ContactPhone,
                command.AddressLine1,
                command.AddressLine2,
                command.City,
                command.PostCode,
                command.DeleteAfter),
            cancellationToken);

        return SetReceiverDetailOutcome.Set;
    }
}
