using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Locations;

namespace UA.Action.Freedom.Api.Locations;

/// <summary>
/// CRUD for distribution hubs (garages/warehouses) and the bays within them. Reads are open to
/// every operational role; writes are Administrator only — setting up a depot is
/// infrastructure, not day-to-day box handling (docs/domain/key-concepts.md § Box).
/// </summary>
public static class LocationEndpoints
{
    public static WebApplication MapFreedomLocations(this WebApplication app)
    {
        var locations = app.MapGroup("/locations").WithTags("Locations");

        locations.MapGet("/", async (
            IQueryHandler<ListLocationsQuery, IReadOnlyList<LocationReadModel>> handler,
            CancellationToken cancellationToken,
            int? page,
            int? pageSize) =>
        {
            var result = await handler.HandleAsync(new ListLocationsQuery(page ?? 1, pageSize ?? 50), cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization(AuthenticationExtensions.LocationsRead);

        locations.MapGet("/{id:int}", async (
            int id,
            IQueryHandler<GetLocationByIdQuery, LocationReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var location = await handler.HandleAsync(new GetLocationByIdQuery(id), cancellationToken);
            return location is null ? Results.NotFound() : Results.Ok(location);
        })
        .RequireAuthorization(AuthenticationExtensions.LocationsRead);

        locations.MapPost("/", async (
            CreateLocationRequest request,
            ICommandHandler<CreateLocationCommand, int> handler,
            CancellationToken cancellationToken) =>
        {
            var id = await handler.HandleAsync(request.ToCommand(), cancellationToken);
            return Results.Created($"/locations/{id}", null);
        })
        .AddEndpointFilter<ValidationFilter<CreateLocationRequest>>()
        .RequireAuthorization(AuthenticationExtensions.LocationsWrite);

        locations.MapPut("/{id:int}", async (
            int id,
            UpdateLocationRequest request,
            ICommandHandler<UpdateLocationCommand, UpdateLocationOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id), cancellationToken);
            return outcome == UpdateLocationOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .AddEndpointFilter<ValidationFilter<UpdateLocationRequest>>()
        .RequireAuthorization(AuthenticationExtensions.LocationsWrite);

        locations.MapDelete("/{id:int}", async (
            int id,
            ICommandHandler<DeleteLocationCommand, DeleteLocationOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new DeleteLocationCommand(id), cancellationToken);
            return outcome == DeleteLocationOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .RequireAuthorization(AuthenticationExtensions.LocationsWrite);

        locations.MapGet("/{id:int}/bays", async (
            int id,
            IQueryHandler<ListBaysQuery, IReadOnlyList<BayReadModel>?> handler,
            CancellationToken cancellationToken) =>
        {
            var bays = await handler.HandleAsync(new ListBaysQuery(id), cancellationToken);
            return bays is null ? Results.NotFound() : Results.Ok(bays);
        })
        .RequireAuthorization(AuthenticationExtensions.LocationsRead);

        locations.MapPost("/{id:int}/bays", async (
            int id,
            CreateBayRequest request,
            ICommandHandler<CreateBayCommand, CreateBayResult> handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

            return result.Outcome switch
            {
                CreateBayOutcome.Created => Results.Created($"/locations/{id}/bays/{result.Id}", null),
                CreateBayOutcome.LocationNotFound => Results.NotFound(),
                _ => Results.Problem(
                    detail: $"A bay with code '{request.Code}' already exists at this location.",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .AddEndpointFilter<ValidationFilter<CreateBayRequest>>()
        .RequireAuthorization(AuthenticationExtensions.LocationsWrite);

        locations.MapPut("/{id:int}/bays/{bayId:int}", async (
            int id,
            int bayId,
            UpdateBayRequest request,
            ICommandHandler<UpdateBayCommand, UpdateBayOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id, bayId), cancellationToken);

            return outcome switch
            {
                UpdateBayOutcome.Updated => Results.NoContent(),
                UpdateBayOutcome.NotFound => Results.NotFound(),
                _ => Results.Problem(
                    detail: $"A bay with code '{request.Code}' already exists at this location.",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .AddEndpointFilter<ValidationFilter<UpdateBayRequest>>()
        .RequireAuthorization(AuthenticationExtensions.LocationsWrite);

        locations.MapDelete("/{id:int}/bays/{bayId:int}", async (
            int id,
            int bayId,
            ICommandHandler<DeleteBayCommand, DeleteBayOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new DeleteBayCommand(bayId), cancellationToken);
            return outcome == DeleteBayOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .RequireAuthorization(AuthenticationExtensions.LocationsWrite);

        return app;
    }
}
