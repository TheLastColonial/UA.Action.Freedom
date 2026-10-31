using Microsoft.Extensions.Options;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Api.Configuration.Scope;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Api.Convoys;

/// <summary>Body of <c>POST /convoys/{id}/route/{pointId}/reached</c> and <c>/crossed</c>. A VIN narrows the mark to one vehicle where vehicles split.</summary>
public sealed record RecordMarkRequest(string? Vin = null);

/// <summary>
/// The Convoy Leader's checklist (ADR 0016, X2, O20, O27): points reached, borders crossed, and what HQ reads back.
/// Progress is reported and never tracked, so no route here takes a position. The leader marks their own convoy and the
/// Dispatcher may mark on their behalf, named in the record; every response is sent <c>no-store</c> (X10).
/// </summary>
public static class ProgressEndpoints
{
    public static WebApplication MapFreedomProgress(this WebApplication app)
    {
        var convoys = app.MapGroup("/convoys").WithTags("Convoy progress");

        convoys.MapGet("/{id:int}/progress", async (
            int id,
            IQueryHandler<GetConvoyProgressQuery, ConvoyProgressReadModel?> handler,
            IOptions<AppOptions> options,
            CancellationToken cancellationToken) =>
        {
            var progress = await handler.HandleAsync(new GetConvoyProgressQuery(id), cancellationToken);
            return progress is null
                ? Results.NotFound()
                : Results.Ok(progress with { HqPhone = options.Value.HqPhone });
        })
        .RequireConvoyScope()
        .RequireAuthorization(AuthenticationExtensions.ConvoysReadLed);

        convoys.MapPost("/{id:int}/route/{pointId:int}/reached", (
            int id, int pointId, RecordMarkRequest? request,
            ICommandHandler<RecordRoutePointMarkCommand, RecordRoutePointMarkOutcome> handler,
            CancellationToken cancellationToken) =>
            RecordAsync(handler, new RecordRoutePointMarkCommand(id, pointId, RoutePointMarkKind.Reached, request?.Vin), cancellationToken))
        .RequireConvoyScope()
        .RequireAuthorization(AuthenticationExtensions.ConvoysReportProgress);

        convoys.MapPost("/{id:int}/route/{pointId:int}/crossed", (
            int id, int pointId, RecordMarkRequest? request,
            ICommandHandler<RecordRoutePointMarkCommand, RecordRoutePointMarkOutcome> handler,
            CancellationToken cancellationToken) =>
            RecordAsync(handler, new RecordRoutePointMarkCommand(id, pointId, RoutePointMarkKind.Crossed, request?.Vin), cancellationToken))
        .RequireConvoyScope()
        .RequireAuthorization(AuthenticationExtensions.ConvoysReportProgress);

        return app;
    }

    private static async Task<IResult> RecordAsync(
        ICommandHandler<RecordRoutePointMarkCommand, RecordRoutePointMarkOutcome> handler,
        RecordRoutePointMarkCommand command,
        CancellationToken cancellationToken)
    {
        var outcome = await handler.HandleAsync(command, cancellationToken);

        return outcome switch
        {
            RecordRoutePointMarkOutcome.Recorded => Results.NoContent(),
            RecordRoutePointMarkOutcome.ConvoyNotFound => Results.NotFound(),
            RecordRoutePointMarkOutcome.RoutePointNotFound => Results.Problem(
                detail: "That point is not on this convoy's route.",
                statusCode: StatusCodes.Status404NotFound),
            RecordRoutePointMarkOutcome.VehicleNotOnConvoy => Results.Problem(
                detail: "That vehicle is not travelling with this convoy.",
                statusCode: StatusCodes.Status404NotFound),
            RecordRoutePointMarkOutcome.ConvoyNotDeparted => Results.Problem(
                detail: "This convoy has not departed, so nothing can have been reached yet.",
                type: "convoy-not-departed",
                statusCode: StatusCodes.Status409Conflict),
            RecordRoutePointMarkOutcome.AlreadyMarked => Results.Problem(
                detail: "That has already been marked.",
                type: "already-marked",
                statusCode: StatusCodes.Status409Conflict),
            RecordRoutePointMarkOutcome.CrossingNeedsBorder => Results.Problem(
                detail: "Only a border can be crossed.",
                type: "crossing-needs-border",
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => LoginNotLinked.Problem(),
        };
    }
}
