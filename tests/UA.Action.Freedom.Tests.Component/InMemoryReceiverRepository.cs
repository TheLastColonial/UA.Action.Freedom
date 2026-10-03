using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Application.Receivers;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Dictionary-backed receiver persistence so the endpoint tests run without a database.
/// </summary>
internal sealed class InMemoryReceiverRepository : IReceiverRepository, IRecordsWhoChanged
{
    private readonly Dictionary<Guid, ReceiverReadModel> store = [];

    private readonly ChangeLedger<Guid> changes = new();

    public void Attach(IChangeAttribution attribution, IPersonRepository people) =>
        changes.Attach(attribution, people);

    private ReceiverReadModel Read(ReceiverReadModel receiver)
    {
        var (name, at) = changes.Of(receiver.Ref);
        return receiver with { LastChangedByName = name, LastChangedAt = at };
    }

    public InMemoryReceiverRepository(params ReceiverReadModel[] seed)
    {
        foreach (var receiver in seed)
        {
            store[receiver.Ref] = receiver;
        }
    }

    public int Count => store.Count;

    public bool Contains(Guid receiverRef) => store.ContainsKey(receiverRef);

    public Task<ReceiverReadModel?> GetByRefAsync(Guid receiverRef, CancellationToken cancellationToken) =>
        Task.FromResult(store.TryGetValue(receiverRef, out var receiver) ? Read(receiver) : null);

    public Task<IReadOnlyList<ReceiverReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ReceiverReadModel>>(
            store.Values
                .OrderBy(receiver => receiver.Organisation, StringComparer.Ordinal)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(Read)
                .ToList());

    public Task<bool> ExistsAsync(Guid receiverRef, CancellationToken cancellationToken) =>
        Task.FromResult(store.ContainsKey(receiverRef));

    public Task AddAsync(ReceiverReadModel receiver, CancellationToken cancellationToken)
    {
        store[receiver.Ref] = receiver;
        changes.Stamp(receiver.Ref);
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(ReceiverReadModel receiver, CancellationToken cancellationToken)
    {
        if (!store.ContainsKey(receiver.Ref))
        {
            return Task.FromResult(false);
        }

        store[receiver.Ref] = receiver;
        changes.Stamp(receiver.Ref);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid receiverRef, CancellationToken cancellationToken)
    {
        changes.Forget(receiverRef);
        return Task.FromResult(store.Remove(receiverRef));
    }
}

/// <summary>
/// Stands in for the Ground Officer database identity, recording every resolve the way the real
/// repository records it — so the endpoint tests can assert that the audit trail is written.
/// </summary>
internal sealed class InMemoryReceiverDetailRepository : IReceiverDetailRepository
{
    private readonly Dictionary<Guid, ReceiverDetailReadModel> store = [];

    public InMemoryReceiverDetailRepository(params ReceiverDetailReadModel[] seed)
    {
        foreach (var detail in seed)
        {
            store[detail.Ref] = detail;
        }
    }

    /// <summary>Every resolve attempt, as (receiver, who asked, why).</summary>
    public List<(Guid Ref, Guid PersonId, string? Reason)> AccessLog { get; } = [];

    public bool Contains(Guid receiverRef) => store.ContainsKey(receiverRef);

    public Task<ReceiverDetailReadModel?> ResolveAsync(
        Guid receiverRef, Guid personId, string? reason, CancellationToken cancellationToken)
    {
        AccessLog.Add((receiverRef, personId, reason));
        return Task.FromResult(store.GetValueOrDefault(receiverRef));
    }

    public Task UpsertAsync(ReceiverDetailReadModel detail, CancellationToken cancellationToken)
    {
        store[detail.Ref] = detail;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid receiverRef, CancellationToken cancellationToken) =>
        Task.FromResult(store.Remove(receiverRef));

    public Task<int> CountAccessesAsync(Guid receiverRef, CancellationToken cancellationToken) =>
        Task.FromResult(AccessLog.Count(entry => entry.Ref == receiverRef));
}
