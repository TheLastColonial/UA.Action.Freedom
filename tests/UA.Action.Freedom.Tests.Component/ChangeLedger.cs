using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// A fake that records who last changed each of its rows, as the SQL repositories do in the same
/// statement as the change. <see cref="FreedomApi"/> attaches the request's attribution and the
/// roster to it, so a fake stamps and names the caller exactly as production does.
/// </summary>
internal interface IRecordsWhoChanged
{
    void Attach(IChangeAttribution attribution, IPersonRepository people);
}

/// <summary>
/// How a volunteer is shown wherever a record names one — the in-memory twin of <c>dbo.PersonDisplay</c>.
/// A volunteer no longer on file (erased) is "Former volunteer": the one rule, shared by every fake.
/// </summary>
internal static class PersonDisplay
{
    public static readonly (string FirstName, string LastName) Erased = ("Former", "volunteer");

    public static string Name((string FirstName, string LastName) person) => $"{person.FirstName} {person.LastName}";
}

/// <summary>
/// The rows' <c>LastChangedBy</c>/<c>LastChangedAt</c>, and the one display rule for the person:
/// a volunteer reads by name, and one no longer on file — erased — reads "Former volunteer", which
/// is what <c>dbo.PersonDisplay</c> does with its LEFT JOIN.
/// </summary>
internal sealed class ChangeLedger<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, (Guid? By, DateTime At)> stamps;
    private IChangeAttribution? attribution;
    private IPersonRepository? people;

    public ChangeLedger(IEqualityComparer<TKey>? comparer = null) => stamps = new(comparer);

    public void Attach(IChangeAttribution newAttribution, IPersonRepository roster)
    {
        attribution = newAttribution;
        people = roster;
    }

    public void Stamp(TKey key) => stamps[key] = (attribution?.PersonId, DateTime.UtcNow);

    public void Forget(TKey key) => stamps.Remove(key);

    public (string? Name, DateTime? At) Of(TKey key)
    {
        if (!stamps.TryGetValue(key, out var stamp))
        {
            return (null, null);
        }

        return (NameOf(stamp.By), stamp.At);
    }

    private string? NameOf(Guid? personId)
    {
        if (personId is not { } id)
        {
            return null;
        }

        var person = people?.GetByIdAsync(id, CancellationToken.None).GetAwaiter().GetResult();
        return PersonDisplay.Name(person is null ? PersonDisplay.Erased : (person.FirstName, person.LastName));
    }
}
