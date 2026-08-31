using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// A dictionary-backed <see cref="IPersonRepository"/> so the endpoint tests run without a
/// database. The Dapper implementation is covered separately by the integration tests.
/// </summary>
internal sealed class InMemoryPersonRepository : IPersonRepository
{
    private readonly Dictionary<Guid, PersonReadModel> store = [];

    public InMemoryPersonRepository(params PersonReadModel[] seed)
    {
        foreach (var person in seed)
        {
            store[person.Id] = person;
        }
    }

    public int Count => store.Count;

    public bool Contains(Guid id) => store.ContainsKey(id);

    public PersonReadModel Single() => store.Values.Single();

    public Task<PersonReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.GetValueOrDefault(id));

    public Task<IReadOnlyList<PersonReadModel>> ListAsync(
        int page, int pageSize, bool driversOnly, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PersonReadModel>>(
            store.Values
                .Where(person => !driversOnly || person.IsDriver)
                .OrderBy(person => person.LastName, StringComparer.Ordinal)
                .ThenBy(person => person.FirstName, StringComparer.Ordinal)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList());

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.ContainsKey(id));

    public Task AddAsync(PersonReadModel person, CancellationToken cancellationToken)
    {
        store[person.Id] = person;
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(PersonReadModel person, CancellationToken cancellationToken)
    {
        if (!store.ContainsKey(person.Id))
        {
            return Task.FromResult(false);
        }

        store[person.Id] = person;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.Remove(id));
}
