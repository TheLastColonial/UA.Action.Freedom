using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Api.Configuration;

/// <summary>
/// The refusal for a write that records who did it, made from a login no Administrator has
/// linked to a volunteer. Never a fallback identity: the act is refused and nothing is written.
/// </summary>
internal static class LoginNotLinked
{
    public const string ProblemType = "login-not-linked";

    public static IResult Problem() => Results.Problem(
        type: ProblemType,
        title: "This login is not linked to a volunteer.",
        detail: "This action is recorded against the person who did it. Ask an Administrator to link your " +
                "login to your volunteer record.",
        statusCode: StatusCodes.Status403Forbidden);

    /// <summary>The linked caller, or the 403 to return when there is none.</summary>
    public static async Task<(Guid? PersonId, IResult? Refusal)> RequireAsync(
        ICurrentPerson currentPerson, CancellationToken cancellationToken) =>
        await currentPerson.ResolveAsync(cancellationToken) is CurrentPerson.Linked(var personId)
            ? (personId, null)
            : (null, Problem());
}
