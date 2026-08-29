using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Boxes;

namespace UA.Action.Freedom.Api.Boxes;

/// <summary>
/// Boxes and their contents. Reads are open to every operational role; packing is
/// Administrator, Dispatcher and Loader; validating is Administrator and Loader.
/// </summary>
/// <remarks>
/// Validation is a <c>POST</c> to its own path rather than a field on the box body, and it is
/// the only way weight is ever written. It happens once: a Loader checks the contents, weighs
/// the box and signs for it, and from then on the box is frozen — no items in or out, no change
/// of receiver — because any of those would leave a confirmed weight describing something that
/// is no longer true (docs/domain/key-concepts.md § Box).
/// </remarks>
public static class BoxEndpoints
{
    private const string ValidatedProblem =
        "This box has been validated. Its contents and weight were confirmed by a Loader and can no longer change.";

    public static WebApplication MapFreedomBoxes(this WebApplication app)
    {
        var boxes = app.MapGroup("/boxes").WithTags("Boxes");

        boxes.MapGet("/", async (
            IQueryHandler<ListBoxesQuery, IReadOnlyList<BoxReadModel>> handler,
            CancellationToken cancellationToken,
            int? page,
            int? pageSize) =>
        {
            var result = await handler.HandleAsync(new ListBoxesQuery(page ?? 1, pageSize ?? 50), cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization(AuthenticationExtensions.BoxesRead);

        boxes.MapGet("/{id:int}", async (
            int id,
            IQueryHandler<GetBoxByIdQuery, BoxReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var box = await handler.HandleAsync(new GetBoxByIdQuery(id), cancellationToken);
            return box is null ? Results.NotFound() : Results.Ok(box);
        })
        .RequireAuthorization(AuthenticationExtensions.BoxesRead);

        boxes.MapPost("/", async (
            CreateBoxRequest request,
            ICommandHandler<CreateBoxCommand, int> handler,
            CancellationToken cancellationToken) =>
        {
            var id = await handler.HandleAsync(request.ToCommand(), cancellationToken);
            return Results.Created($"/boxes/{id}", null);
        })
        .AddEndpointFilter<ValidationFilter<CreateBoxRequest>>()
        .RequireAuthorization(AuthenticationExtensions.BoxesWrite);

        boxes.MapPut("/{id:int}", async (
            int id,
            UpdateBoxRequest request,
            ICommandHandler<UpdateBoxCommand, UpdateBoxOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

            return outcome switch
            {
                UpdateBoxOutcome.Updated => Results.NoContent(),
                UpdateBoxOutcome.NotFound => Results.NotFound(),
                _ => Results.Problem(detail: ValidatedProblem, statusCode: StatusCodes.Status409Conflict),
            };
        })
        .AddEndpointFilter<ValidationFilter<UpdateBoxRequest>>()
        .RequireAuthorization(AuthenticationExtensions.BoxesWrite);

        boxes.MapDelete("/{id:int}", async (
            int id,
            ICommandHandler<DeleteBoxCommand, DeleteBoxOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new DeleteBoxCommand(id), cancellationToken);
            return outcome == DeleteBoxOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .RequireAuthorization(AuthenticationExtensions.BoxesWrite);

        boxes.MapPost("/{id:int}/validate", async (
            int id,
            ValidateBoxRequest request,
            ICommandHandler<ValidateBoxCommand, ValidateBoxOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

            return outcome switch
            {
                ValidateBoxOutcome.Validated => Results.NoContent(),
                ValidateBoxOutcome.NotFound => Results.NotFound(),
                ValidateBoxOutcome.NoSuchValidator => Results.Problem(
                    detail: "The volunteer named as having checked this box is not on file.",
                    statusCode: StatusCodes.Status404NotFound),
                _ => Results.Problem(
                    detail: "This box has already been validated.",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .AddEndpointFilter<ValidationFilter<ValidateBoxRequest>>()
        .RequireAuthorization(AuthenticationExtensions.BoxesValidate);

        boxes.MapGet("/{id:int}/items", async (
            int id,
            IQueryHandler<ListBoxItemsQuery, IReadOnlyList<BoxItemReadModel>?> handler,
            CancellationToken cancellationToken) =>
        {
            // Null means no such box; an empty list means a box nobody has packed yet.
            var items = await handler.HandleAsync(new ListBoxItemsQuery(id), cancellationToken);
            return items is null ? Results.NotFound() : Results.Ok(items);
        })
        .RequireAuthorization(AuthenticationExtensions.BoxesRead);

        boxes.MapPost("/{id:int}/items", async (
            int id,
            AddBoxItemRequest request,
            ICommandHandler<AddBoxItemCommand, AddBoxItemOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

            return outcome switch
            {
                AddBoxItemOutcome.Added => Results.NoContent(),
                AddBoxItemOutcome.BoxNotFound => Results.NotFound(),
                _ => Results.Problem(detail: ValidatedProblem, statusCode: StatusCodes.Status409Conflict),
            };
        })
        .AddEndpointFilter<ValidationFilter<AddBoxItemRequest>>()
        .RequireAuthorization(AuthenticationExtensions.BoxesWrite);

        boxes.MapDelete("/{id:int}/items/{itemId:guid}", async (
            int id,
            Guid itemId,
            ICommandHandler<RemoveBoxItemCommand, RemoveBoxItemOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new RemoveBoxItemCommand(id, itemId), cancellationToken);

            return outcome switch
            {
                RemoveBoxItemOutcome.Removed => Results.NoContent(),
                RemoveBoxItemOutcome.NotFound => Results.NotFound(),
                _ => Results.Problem(detail: ValidatedProblem, statusCode: StatusCodes.Status409Conflict),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.BoxesWrite);

        return app;
    }
}
