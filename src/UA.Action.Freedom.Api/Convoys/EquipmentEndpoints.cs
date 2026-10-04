using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Api.Convoys;

/// <summary>
/// Equipment the charity buys for a vehicle (O13), and the catalogue it is chosen from. Reads are <c>convoys:read</c>,
/// writes <c>convoys:write</c>. It is accounted for separately from donations: no donor, not part of the value delivered.
/// </summary>
public static class EquipmentEndpoints
{
    public static WebApplication MapFreedomEquipment(this WebApplication app)
    {
        app.MapGet("/equipment-items", async (
            IQueryHandler<ListEquipmentItemsQuery, IReadOnlyList<EquipmentItemReadModel>> handler,
            CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(new ListEquipmentItemsQuery(), cancellationToken)))
        .WithTags("Vehicle equipment")
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        app.MapPost("/equipment-items", async (
            AddEquipmentItemRequest request,
            ICommandHandler<AddEquipmentItemCommand, AddEquipmentItemResult> handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

            return result.Outcome == AddEquipmentItemOutcome.Created
                ? Results.Created($"/equipment-items/{result.Id}", new { id = result.Id })
                : Results.Problem(
                    detail: "That item is already in the equipment catalogue.",
                    statusCode: StatusCodes.Status409Conflict);
        })
        .AddEndpointFilter<ValidationFilter<AddEquipmentItemRequest>>()
        .WithTags("Vehicle equipment")
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        var vehicle = app.MapGroup("/convoys/{id:int}/vehicles/{vin}/equipment").WithTags("Vehicle equipment");

        vehicle.MapGet("/", async (
            int id,
            string vin,
            IQueryHandler<GetVehicleEquipmentQuery, IReadOnlyList<VehicleEquipmentReadModel>?> handler,
            CancellationToken cancellationToken) =>
        {
            var lines = await handler.HandleAsync(new GetVehicleEquipmentQuery(id, vin), cancellationToken);
            return lines is null ? Results.NotFound() : Results.Ok(lines);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        vehicle.MapPut("/", async (
            int id,
            string vin,
            SetVehicleEquipmentRequest request,
            ICommandHandler<SetVehicleEquipmentCommand, SetVehicleEquipmentOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id, vin), cancellationToken);

            return outcome switch
            {
                SetVehicleEquipmentOutcome.Set => Results.NoContent(),
                SetVehicleEquipmentOutcome.ConvoyNotFound => Results.NotFound(),
                SetVehicleEquipmentOutcome.ConvoyArrived => Results.Problem(
                    detail: "This convoy has arrived, so its equipment can no longer change.",
                    statusCode: StatusCodes.Status409Conflict),
                SetVehicleEquipmentOutcome.UnknownItem => Results.Problem(
                    detail: "An equipment item is not in the catalogue.",
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                _ => Results.Problem(
                    detail: $"There is no vehicle with VIN '{vin}' on this convoy.",
                    statusCode: StatusCodes.Status404NotFound),
            };
        })
        .AddEndpointFilter<ValidationFilter<SetVehicleEquipmentRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        return app;
    }
}
