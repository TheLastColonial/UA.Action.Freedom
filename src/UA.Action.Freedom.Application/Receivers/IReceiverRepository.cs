using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Receivers;

/// <summary>
/// Persistence port for <see cref="ReceiverReadModel"/> — the non-sensitive half, in
/// <c>dbo.Receiver</c>, reachable by the application's own database identity.
/// </summary>
public interface IReceiverRepository
{
    Task<ReceiverReadModel?> GetByRefAsync(Guid receiverRef, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReceiverReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid receiverRef, CancellationToken cancellationToken);

    Task AddAsync(ReceiverReadModel receiver, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(ReceiverReadModel receiver, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid receiverRef, CancellationToken cancellationToken);

    /// <summary>
    /// Records the registration status. Returns false when there is no such receiver. Nothing else
    /// writes it: an ordinary update cannot set or clear it.
    /// </summary>
    Task<bool> SetStatusAsync(Guid receiverRef, ReceiverStatus status, CancellationToken cancellationToken);

    /// <summary>The boxes and convoys that currently name this receiver. Identifiers only.</summary>
    Task<ReceiverUsageReadModel> GetUsageAsync(Guid receiverRef, CancellationToken cancellationToken);
}

/// <summary>
/// What a status change touches: the boxes whose destination is this receiver, and the convoys that
/// carry those boxes or hand a vehicle over to it. Identifiers and counts only — never an address.
/// </summary>
public sealed record ReceiverUsageReadModel(IReadOnlyList<int> BoxIds, IReadOnlyList<int> ConvoyIds)
{
    public int BoxCount => BoxIds.Count;

    public int ConvoyCount => ConvoyIds.Count;
}

/// <summary>
/// Persistence port for <see cref="ReceiverDetailReadModel"/> — the segregated half, in the
/// <c>sensitive</c> schema, reachable only by the Ground Officer database identity.
/// </summary>
/// <remarks>
/// A separate port from <see cref="IReceiverRepository"/> on purpose. The two halves are
/// reached by different database principals, and a single repository spanning both would put
/// the code that may read an address next to the code that may not.
/// </remarks>
public interface IReceiverDetailRepository
{
    /// <summary>
    /// Resolves a receiver's full delivery detail, recording who asked and why.
    /// </summary>
    /// <remarks>
    /// The audit is a parameter of the read, not an optional call afterwards, because §4.4.3
    /// makes the trail matter more than the data: there is no way to spell "read the address
    /// but do not log it". The implementation writes the log row in the same transaction as
    /// the select, so a read cannot be committed without its audit entry.
    /// </remarks>
    Task<ReceiverDetailReadModel?> ResolveAsync(
        Guid receiverRef, Guid personId, string? reason, CancellationToken cancellationToken);

    Task UpsertAsync(ReceiverDetailReadModel detail, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid receiverRef, CancellationToken cancellationToken);

    /// <summary>How many times this receiver's address has been resolved. For the audit view.</summary>
    Task<int> CountAccessesAsync(Guid receiverRef, CancellationToken cancellationToken);
}
