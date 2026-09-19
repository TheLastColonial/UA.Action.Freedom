using System.Security.Claims;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

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
            return outcome switch
            {
                DeleteConvoyOutcome.Deleted => Results.NoContent(),
                DeleteConvoyOutcome.StillReferenced => Results.Problem(
                    detail: "This convoy has manifests, which are the record of its journey, so it cannot be removed.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.NotFound(),
            };
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
                AssignVehicleOutcome.VehicleNotPassedInspection => Results.Problem(
                    detail: $"Vehicle '{vin}' has not passed its servicing inspection, so it cannot join a convoy.",
                    statusCode: StatusCodes.Status409Conflict),
                AssignVehicleOutcome.VehicleOnAnotherConvoy => Results.Problem(
                    detail: $"Vehicle '{vin}' is already on another convoy. Remove it from that convoy first.",
                    statusCode: StatusCodes.Status409Conflict),
                AssignVehicleOutcome.VehicleHandedOver => Results.Problem(
                    detail: $"Vehicle '{vin}' was handed over in Ukraine at the end of an earlier convoy.",
                    statusCode: StatusCodes.Status409Conflict),
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

        convoys.MapGet("/{id:int}/vehicles/{vin}/drivers", async (
            int id,
            string vin,
            IQueryHandler<ListVehicleDriversQuery, IReadOnlyList<VehicleDriverReadModel>?> handler,
            CancellationToken cancellationToken) =>
        {
            var drivers = await handler.HandleAsync(new ListVehicleDriversQuery(id, vin), cancellationToken);
            return drivers is null ? Results.NotFound() : Results.Ok(drivers);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapPut("/{id:int}/vehicles/{vin}/drivers/{personId:guid}", async (
            int id,
            string vin,
            Guid personId,
            AssignCrewRequest? request,
            ICommandHandler<AssignDriverToVehicleCommand, AssignDriverOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            // The body is optional: no body means a driver, which is what every earlier caller meant.
            var role = request?.Role ?? CrewRole.Driver;
            var outcome = await handler.HandleAsync(new AssignDriverToVehicleCommand(id, vin, personId, role), cancellationToken);

            return outcome switch
            {
                AssignDriverOutcome.Assigned => Results.NoContent(),
                AssignDriverOutcome.ConvoyNotFound => Results.NotFound(),
                AssignDriverOutcome.VehicleNotFound => Results.Problem(
                    detail: $"There is no vehicle with VIN '{vin}' on this convoy.",
                    statusCode: StatusCodes.Status404NotFound),
                AssignDriverOutcome.PersonNotFound => Results.Problem(
                    detail: $"There is no volunteer with that ID.",
                    statusCode: StatusCodes.Status404NotFound),
                AssignDriverOutcome.PersonNotADriver => Results.Problem(
                    detail: "That volunteer is not registered as a driver. They can ride as a passenger instead.",
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                AssignDriverOutcome.ConvoyArrived => ConvoyArrived(),
                AssignDriverOutcome.OnAnotherVehicle => Results.Problem(
                    detail: "That volunteer is already crewing another vehicle on this convoy. A person takes one seat per convoy.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.Problem(
                    detail: "That driver is already assigned to this vehicle.",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysAssignDrivers);

        convoys.MapDelete("/{id:int}/vehicles/{vin}/drivers/{personId:guid}", async (
            int id,
            string vin,
            Guid personId,
            ICommandHandler<UnassignDriverFromVehicleCommand, UnassignDriverOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new UnassignDriverFromVehicleCommand(id, vin, personId), cancellationToken);

            return outcome switch
            {
                UnassignDriverOutcome.Unassigned => Results.NoContent(),
                UnassignDriverOutcome.ConvoyNotFound => Results.NotFound(),
                UnassignDriverOutcome.ConvoyArrived => ConvoyArrived(),
                UnassignDriverOutcome.NotOnThisConvoy => Results.Problem(
                    detail: $"There is no vehicle with VIN '{vin}' on this convoy.",
                    statusCode: StatusCodes.Status404NotFound),
                _ => Results.NotFound(),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysAssignDrivers);

        convoys.MapGet("/{id:int}/vehicles/{vin}/insurance", async (
            int id,
            string vin,
            IQueryHandler<GetInsuranceQuery, VehicleInsuranceReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var policy = await handler.HandleAsync(new GetInsuranceQuery(id, vin), cancellationToken);
            return policy is null ? Results.NotFound() : Results.Ok(policy);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapPut("/{id:int}/vehicles/{vin}/insurance", async (
            int id,
            string vin,
            RecordInsuranceRequest request,
            ClaimsPrincipal caller,
            ICommandHandler<RecordInsuranceCommand, RecordInsuranceOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            // Who recorded the policy comes from the token, never the body.
            var recordedBy = caller.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? caller.FindFirstValue("sub")
                             ?? "unknown";

            var outcome = await handler.HandleAsync(request.ToCommand(id, vin, recordedBy), cancellationToken);

            return outcome switch
            {
                RecordInsuranceOutcome.Recorded => Results.NoContent(),
                RecordInsuranceOutcome.ConvoyNotFound => Results.NotFound(),
                RecordInsuranceOutcome.ConvoyArrived => ConvoyArrived(),
                _ => Results.Problem(
                    detail: $"There is no vehicle with VIN '{vin}' on this convoy.",
                    statusCode: StatusCodes.Status404NotFound),
            };
        })
        .AddEndpointFilter<ValidationFilter<RecordInsuranceRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapDelete("/{id:int}/vehicles/{vin}/insurance", async (
            int id,
            string vin,
            ICommandHandler<RemoveInsuranceCommand, RemoveInsuranceOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new RemoveInsuranceCommand(id, vin), cancellationToken);

            return outcome switch
            {
                RemoveInsuranceOutcome.Removed => Results.NoContent(),
                RemoveInsuranceOutcome.ConvoyArrived => ConvoyArrived(),
                _ => Results.NotFound(),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapGet("/{id:int}/readiness", async (
            int id,
            IQueryHandler<GetConvoyReadinessQuery, ConvoyReadinessReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            // Advisory: it says what is missing, it blocks nothing.
            var readiness = await handler.HandleAsync(new GetConvoyReadinessQuery(id), cancellationToken);
            return readiness is null ? Results.NotFound() : Results.Ok(readiness);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapPost("/{id:int}/arrive", async (
            int id,
            ICommandHandler<ArriveConvoyCommand, ArriveConvoyResult> handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new ArriveConvoyCommand(id), cancellationToken);

            return result.Outcome switch
            {
                ArriveConvoyOutcome.Arrived => Results.NoContent(),
                ArriveConvoyOutcome.NotFound => Results.NotFound(),
                ArriveConvoyOutcome.TruckListNotPublished => Results.Problem(
                    detail: "This convoy's truck list was never published, so it has not travelled.",
                    statusCode: StatusCodes.Status409Conflict),
                ArriveConvoyOutcome.AlreadyArrived => Results.Problem(
                    detail: "This convoy has already arrived.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.Problem(
                    detail: "These vehicles have no Delivered, Lost or Returned manifest yet: "
                            + string.Join(", ", result.StillTravelling) + ".",
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

    private static IResult ConvoyArrived() => Results.Problem(
        detail: "This convoy has arrived. Its crew and insurance are a record of the journey and can no longer change.",
        statusCode: StatusCodes.Status409Conflict);
}
