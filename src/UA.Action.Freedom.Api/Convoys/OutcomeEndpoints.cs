using FluentValidation;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Api.Convoys;

/// <summary>
/// Body of <c>POST /convoys/{id}/boxes/{boxId}/outcome</c>. The reason is one of
/// <see cref="BoxOutcomes.UndeliverableReasons"/> and the hub is a registered location; neither is free text or an address.
/// </summary>
public sealed record RecordBoxOutcomeRequest(BoxOutcome Outcome, string? Reason = null, int? HubLocationId = null)
{
    public RecordBoxOutcomeCommand ToCommand(int convoyId, int boxId) => new(convoyId, boxId, Outcome, Reason, HubLocationId);
}

public sealed class RecordBoxOutcomeRequestValidator : AbstractValidator<RecordBoxOutcomeRequest>
{
    public RecordBoxOutcomeRequestValidator()
    {
        RuleFor(r => r.Outcome).IsInEnum();
    }
}

/// <summary>
/// How a load ends on a convoy (ADR 0015, plan 14). The Dispatcher records these for the Convoy Leader until scoped
/// permissions give the leader their own, so every route is <c>convoys:write</c>.
/// </summary>
public static class OutcomeEndpoints
{
    public static WebApplication MapFreedomOutcomes(this WebApplication app)
    {
        var convoys = app.MapGroup("/convoys").WithTags("Convoy outcomes");

        convoys.MapPost("/{id:int}/boxes/{boxId:int}/outcome", async (
            int id,
            int boxId,
            RecordBoxOutcomeRequest request,
            ICommandHandler<RecordBoxOutcomeCommand, RecordBoxOutcomeOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id, boxId), cancellationToken);

            return outcome switch
            {
                RecordBoxOutcomeOutcome.Recorded => Results.Ok(new { boxId, outcome = request.Outcome }),
                RecordBoxOutcomeOutcome.ConvoyNotFound => Results.NotFound(),
                RecordBoxOutcomeOutcome.BoxNotOnConvoy => Results.Problem(
                    detail: "That box is not on a vehicle of this convoy.",
                    statusCode: StatusCodes.Status404NotFound),
                RecordBoxOutcomeOutcome.ConvoyNotDeparted => Results.Problem(
                    detail: "This convoy has not departed, so no box has an outcome yet.",
                    type: "convoy-not-departed",
                    statusCode: StatusCodes.Status409Conflict),
                RecordBoxOutcomeOutcome.IllegalTransition => Results.Problem(
                    detail: "That outcome cannot follow this box's current outcome.",
                    type: "box-outcome-illegal",
                    statusCode: StatusCodes.Status409Conflict),
                RecordBoxOutcomeOutcome.Conflict => Results.Problem(
                    detail: "Somebody else recorded an outcome for this box first. Read it again.",
                    type: "box-outcome-conflict",
                    statusCode: StatusCodes.Status409Conflict),
                RecordBoxOutcomeOutcome.ReasonRequired => Results.Problem(
                    detail: "An undeliverable box needs a reason: " + string.Join(", ", BoxOutcomes.UndeliverableReasons) + ".",
                    type: "box-outcome-reason-required",
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                _ => Results.Problem(
                    detail: "A box can only be returned to a registered hub.",
                    type: "hub-not-registered",
                    statusCode: StatusCodes.Status422UnprocessableEntity),
            };
        })
        .AddEndpointFilter<ValidationFilter<RecordBoxOutcomeRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        // Acceptance is recorded once per Ukrainian goods list (O24); every delivered box on it becomes accepted.
        convoys.MapPost("/{id:int}/vehicles/{vin}/declarations/goods-list/{receiverRef:guid}/accepted", async (
            int id,
            string vin,
            Guid receiverRef,
            ICommandHandler<AcceptGoodsListCommand, AcceptGoodsListResult> handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new AcceptGoodsListCommand(id, vin, receiverRef), cancellationToken);

            return result.Outcome switch
            {
                AcceptGoodsListOutcome.Accepted => Results.Ok(new { boxesAccepted = result.BoxesAccepted }),
                AcceptGoodsListOutcome.ConvoyNotFound => Results.NotFound(),
                AcceptGoodsListOutcome.VehicleNotOnConvoy => Results.Problem(
                    detail: $"There is no vehicle with VIN '{vin}' on this convoy.",
                    statusCode: StatusCodes.Status404NotFound),
                AcceptGoodsListOutcome.ConvoyNotDeparted => Results.Problem(
                    detail: "This convoy has not departed, so nothing has been delivered yet.",
                    type: "convoy-not-departed",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.Problem(
                    detail: "No box for that Receiver is waiting to be accepted on this vehicle.",
                    type: "goods-list-nothing-to-accept",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        return app;
    }
}
