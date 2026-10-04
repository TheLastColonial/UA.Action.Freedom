using System.Security.Claims;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Api.People;

/// <summary>
/// <c>GET /me</c>: who the caller is as the system sees them — their subject and roles, and, once
/// an Administrator has linked their login, their person id and display name. It is open to every
/// authenticated caller (a Ground Officer included) and returns nothing else about the volunteer.
/// </summary>
public static class MeEndpoints
{
    private const string RoleClaimType = "roles";

    public static WebApplication MapFreedomMe(this WebApplication app)
    {
        app.MapGet("/me", async (
            ClaimsPrincipal caller,
            ICurrentPerson currentPerson,
            IQueryHandler<GetPersonByIdQuery, PersonReadModel?> people,
            IScopeAssignments assignments,
            CancellationToken cancellationToken) =>
        {
            var subject = ClaimsCurrentPerson.SubjectOf(caller);
            var roles = caller.FindAll(RoleClaimType).Select(claim => claim.Value).Order().ToArray();

            if (await currentPerson.ResolveAsync(cancellationToken) is not CurrentPerson.Linked(var personId))
            {
                return Results.Ok(new MeResponse(subject, roles, null, null, [], []));
            }

            var person = await people.HandleAsync(new GetPersonByIdQuery(personId), cancellationToken);
            return Results.Ok(new MeResponse(
                subject,
                roles,
                personId,
                person is null ? null : $"{person.FirstName} {person.LastName}",
                await assignments.LedConvoyIdsAsync(personId, cancellationToken),
                await assignments.ManagedLocationIdsAsync(personId, cancellationToken)));
        })
        .WithTags("Me")
        .RequireAuthorization();

        return app;
    }
}

/// <summary>
/// <paramref name="LedConvoyIds"/> and <paramref name="ManagedLocationIds"/> come from the assignment tables on every
/// call (ADR 0010), never from the token, so a reassignment shows at once.
/// </summary>
public sealed record MeResponse(
    string? Subject,
    string[] Roles,
    Guid? PersonId,
    string? DisplayName,
    IReadOnlyList<int> LedConvoyIds,
    IReadOnlyList<int> ManagedLocationIds);
