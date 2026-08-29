using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Api.Convoys;

/// <summary>
/// Convoys, their route, and their truck list. Reads are open to every operational role;
/// writes are Administrator and Dispatcher — planning a convoy and booking its crossings is
/// what the Dispatcher role exists for (docs/domain/key-concepts.md § Roles).
/// </summary>
/// <remarks>
/// Publishing the truck list is a <c>POST</c> to its own path rather than a field on
/// <c>PUT /convoys/{id}</c>. It is a one-way transition that closes the vehicle list, and
/// docs/process.puml puts it between convoy planning and manifest proposal; an ordinary update
/// able to set or clear it would route around that.
/// </remarks>
public static class ConvoyEndpoints
{
    public static WebApplication MapFreedomConvoys(this WebApplication app)
    {
        var convoys = app.MapGroup("/convoys").WithTags("Convoys");

        convoys.MapGet("/", async (
            IQueryHandler<ListConvoysQuery, IReadOnlyList<ConvoyReadModel>> handler,
            CancellationToken cancellationToken,
            int? page,
            int? pageSize) =>
        {
            var result = await handler.HandleAsync(new ListConvoysQuery(page ?? 1, pageSize ?? 50), cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapGet("/{id:int}", async (
            int id,
            IQueryHandler<GetConvoyByIdQuery, ConvoyReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var convoy = await handler.HandleAsync(new GetConvoyByIdQuery(id), cancellationToken);
            return convoy is null ? Results.NotFound() : Results.Ok(convoy);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapPost("/", async (
            CreateConvoyRequest request,
            ICommandHandler<CreateConvoyCommand, int> handler,
            CancellationToken cancellationToken) =>
        {
            var id = await handler.HandleAsync(request.ToCommand(), cancellationToken);
            return Results.Created($"/convoys/{id}", null);
        })
        .AddEndpointFilter<ValidationFilter<CreateConvoyRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapPut("/{id:int}", async (
            int id,
            UpdateConvoyRequest request,
            ICommandHandler<UpdateConvoyCommand, UpdateConvoyOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id), cancellationToken);
            return outcome == UpdateConvoyOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .AddEndpointFilter<ValidationFilter<UpdateConvoyRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapDelete("/{id:int}", async (
            int id,
            ICommandHandler<DeleteConvoyCommand, DeleteConvoyOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new DeleteConvoyCommand(id), cancellationToken);
            return outcome == DeleteConvoyOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapGet("/{id:int}/route", async (
            int id,
            IQueryHandler<GetConvoyRouteQuery, IReadOnlyList<RouteStopReadModel>?> handler,
            CancellationToken cancellationToken) =>
        {
            // Null means no such convoy; an empty list means a convoy whose route is not planned yet.
            var route = await handler.HandleAsync(new GetConvoyRouteQuery(id), cancellationToken);
            return route is null ? Results.NotFound() : Results.Ok(route);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapPut("/{id:int}/route", async (
            int id,
            ReplaceConvoyRouteRequest request,
            ICommandHandler<ReplaceConvoyRouteCommand, ReplaceConvoyRouteOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id), cancellationToken);
            return outcome == ReplaceConvoyRouteOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .AddEndpointFilter<ValidationFilter<ReplaceConvoyRouteRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapGet("/{id:int}/vehicles", async (
            int id,
            IQueryHandler<ListConvoyVehiclesQuery, IReadOnlyList<ConvoyVehicleReadModel>?> handler,
            CancellationToken cancellationToken) =>
        {
            var vehicles = await handler.HandleAsync(new ListConvoyVehiclesQuery(id), cancellationToken);
            return vehicles is null ? Results.NotFound() : Results.Ok(vehicles);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapPut("/{id:int}/vehicles/{vin}", async (
            int id,
            string vin,
            ICommandHandler<AssignVehicleToConvoyCommand, AssignVehicleOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new AssignVehicleToConvoyCommand(id, vin), cancellationToken);

            return outcome switch
            {
                AssignVehicleOutcome.Assigned => Results.NoContent(),
                AssignVehicleOutcome.ConvoyNotFound => Results.NotFound(),
                AssignVehicleOutcome.VehicleNotFound => Results.Problem(
                    detail: $"There is no vehicle with VIN '{vin}'.",
                    statusCode: StatusCodes.Status404NotFound),
                _ => Results.Problem(
                    detail: "The truck list for this convoy has been published, so its vehicles can no longer change.",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapDelete("/{id:int}/vehicles/{vin}", async (
            int id,
            string vin,
            ICommandHandler<UnassignVehicleFromConvoyCommand, UnassignVehicleOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new UnassignVehicleFromConvoyCommand(id, vin), cancellationToken);

            return outcome switch
            {
                UnassignVehicleOutcome.Unassigned => Results.NoContent(),
                UnassignVehicleOutcome.ConvoyNotFound or UnassignVehicleOutcome.NotOnThisConvoy => Results.NotFound(),
                _ => Results.Problem(
                    detail: "The truck list for this convoy has been published, so its vehicles can no longer change.",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapPost("/{id:int}/publish-truck-list", async (
            int id,
            ICommandHandler<PublishTruckListCommand, PublishTruckListOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new PublishTruckListCommand(id), cancellationToken);

            return outcome switch
            {
                PublishTruckListOutcome.Published => Results.NoContent(),
                PublishTruckListOutcome.NotFound => Results.NotFound(),
                _ => Results.Problem(
                    detail: "The truck list for this convoy has already been published.",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        return app;
    }
}
