namespace UA.Action.Freedom.Application.Abstractions;

/// <summary>
/// Who is making the change in progress, for the <c>LastChangedBy</c> every entity row carries.
/// Set once at the edge from the login (never from a request body) and read by the repository
/// that writes the row, so the stamp lands in the same statement as the change and no handler
/// has to remember to pass it.
/// </summary>
/// <remarks>
/// <see cref="PersonId"/> is null only on the few writes an unlinked login may make — creating
/// the volunteer record that login is about to be linked to — where there is no person to name
/// and "unknown" is never invented.
/// </remarks>
public interface IChangeAttribution
{
    Guid? PersonId { get; }
}
