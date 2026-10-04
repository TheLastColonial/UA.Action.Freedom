using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Api.Convoys;

/// <summary>
/// A convoy's budget and the costs entered against it (O12, P3). Reads are <c>convoys:read</c>, writes
/// <c>convoys:write</c>. None of it gates departure (O37): an unset budget or an over-budget line is advice.
/// </summary>
public static class BudgetEndpoints
{
    public static WebApplication MapFreedomBudget(this WebApplication app)
    {
        var convoys = app.MapGroup("/convoys").WithTags("Convoy budget");

        convoys.MapGet("/{id:int}/budget", async (
            int id,
            IQueryHandler<GetBudgetQuery, IReadOnlyList<BudgetLineReadModel>?> handler,
            CancellationToken cancellationToken) =>
        {
            var lines = await handler.HandleAsync(new GetBudgetQuery(id), cancellationToken);
            return lines is null ? Results.NotFound() : Results.Ok(lines);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapPut("/{id:int}/budget", async (
            int id,
            SetBudgetRequest request,
            ICommandHandler<SetBudgetCommand, SetBudgetOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id), cancellationToken);
            return outcome == SetBudgetOutcome.Set ? Results.NoContent() : Results.NotFound();
        })
        .AddEndpointFilter<ValidationFilter<SetBudgetRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapGet("/{id:int}/budget/summary", async (
            int id,
            IQueryHandler<GetBudgetSummaryQuery, BudgetSummaryReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var summary = await handler.HandleAsync(new GetBudgetSummaryQuery(id), cancellationToken);
            return summary is null ? Results.NotFound() : Results.Ok(summary);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapGet("/{id:int}/costs", async (
            int id,
            IQueryHandler<ListCostsQuery, IReadOnlyList<ConvoyCostReadModel>?> handler,
            CancellationToken cancellationToken) =>
        {
            var costs = await handler.HandleAsync(new ListCostsQuery(id), cancellationToken);
            return costs is null ? Results.NotFound() : Results.Ok(costs);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapPost("/{id:int}/costs", async (
            int id,
            AddCostRequest request,
            ICommandHandler<AddCostCommand, AddCostResult> handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

            return result.Outcome switch
            {
                AddCostOutcome.Created => Results.Created($"/convoys/{id}/costs/{result.Id}", new { id = result.Id }),
                AddCostOutcome.ConvoyNotFound => Results.NotFound(),
                AddCostOutcome.VehicleNotOnConvoy => Results.Problem(
                    detail: $"There is no vehicle with VIN '{request.Vin}' on this convoy.",
                    statusCode: StatusCodes.Status404NotFound),
                _ => Results.Problem(
                    detail: "Ferry, hotel and insurance costs are held on their booking or policy and shown from it. Only fuel and other costs are entered.",
                    statusCode: StatusCodes.Status422UnprocessableEntity),
            };
        })
        .AddEndpointFilter<ValidationFilter<AddCostRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapDelete("/{id:int}/costs/{costId:int}", async (
            int id,
            int costId,
            ICommandHandler<DeleteCostCommand, DeleteCostOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new DeleteCostCommand(id, costId), cancellationToken);
            return outcome == DeleteCostOutcome.Deleted ? Results.NoContent() : Results.NotFound();
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        return app;
    }
}
