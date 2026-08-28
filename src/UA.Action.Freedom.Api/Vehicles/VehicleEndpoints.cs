using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Vehicles;

namespace UA.Action.Freedom.Api.Vehicles;

/// <summary>
/// CRUD for donated vehicles. VIN is the natural key: it is the route segment and the one
/// field a <c>PUT</c> cannot change — correcting a VIN is delete-and-recreate. Reads are
/// open to every operational role; writes to Purchaser and Administrator
/// (docs/domain/key-concepts.md § Roles). Ground Officer is excluded from both.
/// </summary>
public static class VehicleEndpoints
{
    public static WebApplication MapFreedomVehicles(this WebApplication app)
    {
        var vehicles = app.MapGroup("/vehicles").WithTags("Vehicles");

        vehicles.MapGet("/", async (
            IQueryHandler<ListVehiclesQuery, IReadOnlyList<VehicleReadModel>> handler,
            CancellationToken cancellationToken,
            int? page,
            int? pageSize) =>
        {
            var result = await handler.HandleAsync(new ListVehiclesQuery(page ?? 1, pageSize ?? 50), cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization(AuthenticationExtensions.VehiclesRead);

        vehicles.MapGet("/{vin}", async (
            string vin,
            IQueryHandler<GetVehicleByVinQuery, VehicleReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var vehicle = await handler.HandleAsync(new GetVehicleByVinQuery(vin), cancellationToken);
            return vehicle is null ? Results.NotFound() : Results.Ok(vehicle);
        })
        .RequireAuthorization(AuthenticationExtensions.VehiclesRead);

        vehicles.MapPost("/", async (
            CreateVehicleRequest request,
            ICommandHandler<CreateVehicleCommand, CreateVehicleOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(), cancellationToken);

            return outcome == CreateVehicleOutcome.Conflict
                ? Results.Problem(
                    detail: $"A vehicle with VIN '{request.Vin}' already exists.",
                    statusCode: StatusCodes.Status409Conflict)
                : Results.Created($"/vehicles/{request.Vin}", null);
        })
        .AddEndpointFilter<ValidationFilter<CreateVehicleRequest>>()
        .RequireAuthorization(AuthenticationExtensions.VehiclesWrite);

        vehicles.MapPut("/{vin}", async (
            string vin,
            UpdateVehicleRequest request,
            ICommandHandler<UpdateVehicleCommand, UpdateVehicleOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(vin), cancellationToken);
            return outcome == UpdateVehicleOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .AddEndpointFilter<ValidationFilter<UpdateVehicleRequest>>()
        .RequireAuthorization(AuthenticationExtensions.VehiclesWrite);

        vehicles.MapDelete("/{vin}", async (
            string vin,
            ICommandHandler<DeleteVehicleCommand, DeleteVehicleOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new DeleteVehicleCommand(vin), cancellationToken);
            return outcome == DeleteVehicleOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .RequireAuthorization(AuthenticationExtensions.VehiclesWrite);

        return app;
    }
}
