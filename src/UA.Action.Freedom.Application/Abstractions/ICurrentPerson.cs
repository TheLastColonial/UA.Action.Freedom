namespace UA.Action.Freedom.Application.Abstractions;

/// <summary>
/// Who is calling, as a volunteer on file. Every "who did this" the system records comes from
/// here, never from a request body or a raw token subject.
/// </summary>
public interface ICurrentPerson
{
    Task<CurrentPerson> ResolveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The caller's login is either linked to a volunteer or it is not. There is no third case, and
/// in particular no placeholder identity: a write that records who did it is refused from a
/// login that is <see cref="NotLinked"/>.
/// </summary>
public abstract record CurrentPerson
{
    private CurrentPerson()
    {
    }

    public sealed record Linked(Guid PersonId) : CurrentPerson;

    public sealed record NotLinked : CurrentPerson;
}
