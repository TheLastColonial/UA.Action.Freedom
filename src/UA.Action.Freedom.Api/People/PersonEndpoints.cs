using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Api.People;

/// <summary>
/// CRUD for the volunteers supporting Ukrainian Action, drivers included. The identifier is
/// minted on create, so unlike <c>/vehicles</c> there is no natural key and no conflict case.
/// Reads are open to every operational role — a dispatcher builds driver teams, a loader needs
/// to know who validated a box. Writes are Administrator only: approving volunteers and
/// revoking access when they leave is the Administrator's job
/// (docs/domain/key-concepts.md § Roles).
/// </summary>
public static class PersonEndpoints
{
    public static WebApplication MapFreedomPeople(this WebApplication app)
    {
        var people = app.MapGroup("/people").WithTags("People");

        people.MapGet("/", async (
            IQueryHandler<ListPeopleQuery, IReadOnlyList<PersonReadModel>> handler,
            CancellationToken cancellationToken,
            int? page,
            int? pageSize,
            bool? driversOnly) =>
        {
            var result = await handler.HandleAsync(
                new ListPeopleQuery(page ?? 1, pageSize ?? 50, driversOnly ?? false), cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization(AuthenticationExtensions.PeopleRead);

        people.MapGet("/{id:guid}", async (
            Guid id,
            IQueryHandler<GetPersonByIdQuery, PersonReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var person = await handler.HandleAsync(new GetPersonByIdQuery(id), cancellationToken);
            return person is null ? Results.NotFound() : Results.Ok(person);
        })
        .RequireAuthorization(AuthenticationExtensions.PeopleRead);

        people.MapPost("/", async (
            CreatePersonRequest request,
            ICommandHandler<CreatePersonCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            var id = await handler.HandleAsync(request.ToCommand(), cancellationToken);
            return Results.Created($"/people/{id}", null);
        })
        .AddEndpointFilter<ValidationFilter<CreatePersonRequest>>()
        .RequireAuthorization(AuthenticationExtensions.PeopleWrite);

        people.MapPut("/{id:guid}", async (
            Guid id,
            UpdatePersonRequest request,
            ICommandHandler<UpdatePersonCommand, UpdatePersonOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id), cancellationToken);
            return outcome == UpdatePersonOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .AddEndpointFilter<ValidationFilter<UpdatePersonRequest>>()
        .RequireAuthorization(AuthenticationExtensions.PeopleWrite);

        people.MapDelete("/{id:guid}", async (
            Guid id,
            ICommandHandler<DeletePersonCommand, DeletePersonOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new DeletePersonCommand(id), cancellationToken);
            return outcome == DeletePersonOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .RequireAuthorization(AuthenticationExtensions.PeopleWrite);

        return app;
    }
}
