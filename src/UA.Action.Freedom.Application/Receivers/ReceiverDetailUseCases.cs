using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Telemetry;

namespace UA.Action.Freedom.Application.Receivers;

/// <summary>
/// Resolve a receiver's full delivery detail. Ground Officer only, and audited.
/// </summary>
/// <param name="PrincipalId">Who is asking — taken from the caller's token, never from the body.</param>
/// <param name="Reason">Why, if they gave one. Free text, recorded verbatim.</param>
public sealed record GetReceiverDetailQuery(Guid Ref, string PrincipalId, string? Reason);

public sealed class GetReceiverDetailHandler(
    IReceiverDetailRepository repository,
    FreedomMetrics? metrics = null)
    : IQueryHandler<GetReceiverDetailQuery, ReceiverDetailReadModel?>
{
    private readonly FreedomMetrics _metrics = metrics ?? FreedomMetrics.Unobserved;

    public async Task<ReceiverDetailReadModel?> HandleAsync(
        GetReceiverDetailQuery query, CancellationToken cancellationToken)
    {
        var detail = await repository.ResolveAsync(query.Ref, query.PrincipalId, query.Reason, cancellationToken);

        // Aggregate only: who asked and about which receiver is in the audit table, written in
        // the same transaction as the read. Duplicating it here would build a second, weaker
        // trail with a 31-day retention.
        _metrics.ReceiverDetailResolved(found: detail is not null);

        return detail;
    }
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
