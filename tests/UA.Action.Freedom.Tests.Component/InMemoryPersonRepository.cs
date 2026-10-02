using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// A dictionary-backed <see cref="IPersonRepository"/> so the endpoint tests run without a
/// database. The Dapper implementation is covered separately by the integration tests.
/// </summary>
internal sealed class InMemoryPersonRepository : IPersonRepository
{
    private readonly Dictionary<Guid, PersonReadModel> store = [];

    /// <summary>Volunteers on a live crew or manifest team, whom erasure refuses.</summary>
    private readonly HashSet<Guid> active = [];

    public InMemoryPersonRepository(params PersonReadModel[] seed)
    {
        foreach (var person in seed)
        {
            store[person.Id] = person;
        }
    }

    public InMemoryPersonRepository OnALiveCrew(Guid id)
    {
        active.Add(id);
        return this;
    }

    /// <summary>Logins by token subject. Erasing a volunteer removes theirs, as deleting the detail row does.</summary>
    private readonly Dictionary<string, Guid> logins = [];

    /// <summary>The subject <see cref="TestAuthHandler"/> gives every caller.</summary>
    public const string TestUserSubject = "test-user";

    public InMemoryPersonRepository LinkedTo(string subject, Guid personId)
    {
        logins[subject] = personId;
        return this;
    }

    public InMemoryPersonRepository WithTestUserLinkedTo(Guid personId) => LinkedTo(TestUserSubject, personId);

    /// <summary>The volunteer <see cref="WithLinkedTestUser"/> puts on file for the caller.</summary>
    public static readonly Guid TestUserId = new("7e57a5e2-0000-4000-8000-000000000001");

    /// <summary>A roster in which the test caller is a volunteer, so writes that record who did them are allowed.</summary>
    public static InMemoryPersonRepository WithLinkedTestUser(params PersonReadModel[] others)
    {
        var testUser = new PersonReadModel(
            TestUserId, "Test", "User", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, false, false);

        return new InMemoryPersonRepository([testUser, .. others]).WithTestUserLinkedTo(TestUserId);
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

    public Task<DeletePersonResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (active.Contains(id) && store.ContainsKey(id))
        {
            return Task.FromResult(DeletePersonResult.StillActive);
        }

        if (!store.Remove(id))
        {
            return Task.FromResult(DeletePersonResult.NotFound);
        }

        foreach (var subject in logins.Where(login => login.Value == id).Select(login => login.Key).ToList())
        {
            logins.Remove(subject);
        }

        return Task.FromResult(DeletePersonResult.Deleted);
    }

    public Task<Guid?> FindBySubjectAsync(string subject, CancellationToken cancellationToken) =>
        Task.FromResult<Guid?>(logins.TryGetValue(subject, out var id) && store.ContainsKey(id) ? id : null);

    public Task<LinkLoginResult> LinkLoginAsync(Guid personId, string subject, CancellationToken cancellationToken)
    {
        if (!store.ContainsKey(personId))
        {
            return Task.FromResult(LinkLoginResult.NotFound);
        }

        if (logins.TryGetValue(subject, out var owner) && owner != personId)
        {
            return Task.FromResult(LinkLoginResult.SubjectInUse);
        }

        foreach (var previous in logins.Where(login => login.Value == personId).Select(login => login.Key).ToList())
        {
            logins.Remove(previous);
        }

        logins[subject] = personId;
        return Task.FromResult(LinkLoginResult.Linked);
    }
}
