using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Categories;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Api.Categories;

/// <summary>
/// The categories donated items are sorted into, and the customs code each maps to per authority (ADR 0014). Reads are
/// open to every operational role; writes are Administrator only, because the mapping decides what is declared at a
/// border (O31).
/// </summary>
public static class CategoryEndpoints
{
    public static WebApplication MapFreedomCategories(this WebApplication app)
    {
        var categories = app.MapGroup("/categories").WithTags("Categories");

        categories.MapGet("/", async (
            IQueryHandler<ListCategoriesQuery, IReadOnlyList<ItemCategoryReadModel>> handler,
            CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(new ListCategoriesQuery(), cancellationToken)))
        .RequireAuthorization(AuthenticationExtensions.CategoriesRead);

        categories.MapGet("/{id:int}", async (
            int id,
            IQueryHandler<GetCategoryByIdQuery, ItemCategoryReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var category = await handler.HandleAsync(new GetCategoryByIdQuery(id), cancellationToken);
            return category is null ? Results.NotFound() : Results.Ok(category);
        })
        .RequireAuthorization(AuthenticationExtensions.CategoriesRead);

        categories.MapPost("/", async (
            CreateCategoryRequest request,
            ICommandHandler<CreateCategoryCommand, CreateCategoryResult> handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

            return result.Outcome == CreateCategoryOutcome.Created
                ? Results.Created($"/categories/{result.Id}", null)
                : NameTaken(request.NameEn);
        })
        .AddEndpointFilter<ValidationFilter<CreateCategoryRequest>>()
        .RequireAuthorization(AuthenticationExtensions.CategoriesWrite);

        categories.MapPut("/{id:int}", async (
            int id,
            UpdateCategoryRequest request,
            ICommandHandler<UpdateCategoryCommand, UpdateCategoryOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

            return outcome switch
            {
                UpdateCategoryOutcome.Updated => Results.NoContent(),
                UpdateCategoryOutcome.NotFound => Results.NotFound(),
                _ => NameTaken(request.NameEn),
            };
        })
        .AddEndpointFilter<ValidationFilter<UpdateCategoryRequest>>()
        .RequireAuthorization(AuthenticationExtensions.CategoriesWrite);

        categories.MapPut("/{id:int}/codes/{authority}", async (
            int id,
            string authority,
            SetCategoryCodeRequest request,
            ICommandHandler<SetCategoryCodeCommand, SetCategoryCodeOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<CustomsAuthority>(authority, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                return Results.Problem(
                    detail: "The authority must be UK, EU or UA.", statusCode: StatusCodes.Status400BadRequest);
            }

            var outcome = await handler.HandleAsync(request.ToCommand(id, parsed), cancellationToken);
            return outcome == SetCategoryCodeOutcome.Set ? Results.NoContent() : Results.NotFound();
        })
        .AddEndpointFilter<ValidationFilter<SetCategoryCodeRequest>>()
        .RequireAuthorization(AuthenticationExtensions.CategoriesWrite);

        return app;
    }

    private static IResult NameTaken(string name) => Results.Problem(
        detail: $"A category named '{name}' already exists.", statusCode: StatusCodes.Status409Conflict);
}
