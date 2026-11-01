using FluentValidation;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Api.Configuration.Scope;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
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
/// How a load ends on a convoy (ADR 0015). Recording an outcome and marking a vehicle delivered are the Convoy Leader's own
/// acts on the convoy they lead (<c>convoys:report-progress</c>, scoped), and the Dispatcher keeps them on their behalf
/// (ADR 0016). Acceptance and closing stay the Dispatcher's.
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
        .RequireConvoyScope()
        .RequireAuthorization(AuthenticationExtensions.ConvoysReportProgress);

        convoys.MapGet("/{id:int}/outcomes", async (
            int id,
            IQueryHandler<ListBoxOutcomesQuery, IReadOnlyList<BoxOutcomeReadModel>?> handler,
            CancellationToken cancellationToken) =>
        {
            var outcomes = await handler.HandleAsync(new ListBoxOutcomesQuery(id), cancellationToken);
            return outcomes is null ? Results.NotFound() : Results.Ok(outcomes);
        })
        .RequireConvoyScope()
        .RequireAuthorization(AuthenticationExtensions.ConvoysReadLed);

        // Closing follows arrival and draws the report (O10). It does not lock the convoy (O25): the report is
        // regenerated from the facts, so pressing it again after a correction simply shows the corrected account.
        convoys.MapPost("/{id:int}/close", async (
            int id,
            ICommandHandler<CloseConvoyCommand, CloseConvoyResult> handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new CloseConvoyCommand(id), cancellationToken);

            return result.Outcome switch
            {
                CloseConvoyOutcome.Closed => Results.Ok(result.Report),
                CloseConvoyOutcome.NotFound => Results.NotFound(),
                _ => Results.Problem(
                    detail: "A convoy can only be closed after it has arrived.",
                    type: "convoy-not-arrived",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapGet("/{id:int}/report", async (
            int id,
            IQueryHandler<GetConvoyReportQuery, ConvoyReportReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var report = await handler.HandleAsync(new GetConvoyReportQuery(id), cancellationToken);
            return report is null ? Results.NotFound() : Results.Ok(report);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        // The vehicle is delivered when the Convoy Leader or the Dispatcher says so (O8); its boxes are delivered with it.
        convoys.MapPost("/{id:int}/vehicles/{vin}/delivered", async (
            int id,
            string vin,
            ICommandHandler<DeliverVehicleCommand, DeliverVehicleResult> handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new DeliverVehicleCommand(id, vin), cancellationToken);

            return result.Outcome switch
            {
                DeliverVehicleOutcome.Delivered => Results.Ok(new { boxesDelivered = result.BoxesDelivered }),
                DeliverVehicleOutcome.ConvoyNotFound => Results.NotFound(),
                DeliverVehicleOutcome.VehicleNotOnConvoy => Results.Problem(
                    detail: $"There is no vehicle with VIN '{vin}' on this convoy.",
                    statusCode: StatusCodes.Status404NotFound),
                DeliverVehicleOutcome.ConvoyNotDeparted => Results.Problem(
                    detail: "This convoy has not departed, so nothing can have been delivered.",
                    type: "convoy-not-departed",
                    statusCode: StatusCodes.Status409Conflict),
                DeliverVehicleOutcome.ConvoyArrived => Results.Problem(
                    detail: "This convoy has arrived.",
                    type: "convoy-arrived",
                    statusCode: StatusCodes.Status409Conflict),
                DeliverVehicleOutcome.VehicleWithdrawn => Results.Problem(
                    detail: "That vehicle has left the convoy.",
                    type: "vehicle-withdrawn",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.Problem(
                    detail: "That vehicle has already been delivered.",
                    type: "vehicle-already-delivered",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .RequireConvoyScope()
        .RequireAuthorization(AuthenticationExtensions.ConvoysReportProgress);

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
