namespace UA.Action.Freedom.Api.Receivers;

/// <summary>
/// The two refusals for naming a receiver that cannot be named: one that does not exist, and one that
/// is not registered (ADR 0012). Shared by every endpoint that takes a receiver as a destination.
/// </summary>
internal static class ReceiverProblems
{
    public const string NotFoundType = "receiver-not-found";

    public const string NotRegisteredType = "receiver-not-registered";

    public static IResult NotFound() => Results.Problem(
        type: NotFoundType,
        title: "There is no such receiver.",
        detail: "The receiver named does not exist.",
        statusCode: StatusCodes.Status422UnprocessableEntity);

    public static IResult NotRegistered() => Results.Problem(
        type: NotRegisteredType,
        title: "That receiver is not registered.",
        detail: "Only a registered receiver can be a destination. An Administrator registers a receiver.",
        statusCode: StatusCodes.Status409Conflict);
}
