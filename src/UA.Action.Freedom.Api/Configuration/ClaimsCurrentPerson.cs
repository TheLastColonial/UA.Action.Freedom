using System.Security.Claims;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Api.Configuration;

/// <summary>
/// Resolves the caller from the token subject (<c>NameIdentifier</c>, then <c>sub</c>) through
/// the people port. Scoped per request and memoised, so a handler that asks twice costs one
/// lookup. A missing subject, an unlinked login and an erased volunteer all read as
/// <see cref="CurrentPerson.NotLinked"/>.
/// </summary>
public sealed class ClaimsCurrentPerson(IHttpContextAccessor accessor, IPersonRepository people) : ICurrentPerson
{
    private CurrentPerson? resolved;

    public static string? SubjectOf(ClaimsPrincipal? caller) =>
        caller?.FindFirstValue(ClaimTypes.NameIdentifier) ?? caller?.FindFirstValue("sub");

    public async Task<CurrentPerson> ResolveAsync(CancellationToken cancellationToken)
    {
        if (resolved is not null)
        {
            return resolved;
        }

        var subject = SubjectOf(accessor.HttpContext?.User);
        var personId = string.IsNullOrWhiteSpace(subject)
            ? null
            : await people.FindBySubjectAsync(subject, cancellationToken);

        return resolved = personId is { } id ? new CurrentPerson.Linked(id) : new CurrentPerson.NotLinked();
    }
}
