using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Donations;

namespace UA.Action.Freedom.Api.Donations;

/// <summary>
/// Donors and their donations (ADR 0013). A donor is a split identity with no login, entered by a Dispatcher or
/// Loader (O22): reads are open to every operational role, writes to the roles who take donations in, and erasure
/// to the Administrator alone, as for volunteers. The Ground Officer is excluded from all of it. Erasure is never
/// refused, because a donor has no operational dependency.
/// </summary>
public static class DonationEndpoints
{
    public static WebApplication MapFreedomDonations(this WebApplication app)
    {
        var donors = app.MapGroup("/donors").WithTags("Donors");

        donors.MapGet("/", async (
            IQueryHandler<ListDonorsQuery, IReadOnlyList<DonorReadModel>> handler,
            CancellationToken cancellationToken,
            int? page,
            int? pageSize) =>
            Results.Ok(await handler.HandleAsync(new ListDonorsQuery(page ?? 1, pageSize ?? 50), cancellationToken)))
        .RequireAuthorization(AuthenticationExtensions.DonationsRead);

        donors.MapGet("/{id:guid}", async (
            Guid id,
            IQueryHandler<GetDonorByIdQuery, DonorReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var donor = await handler.HandleAsync(new GetDonorByIdQuery(id), cancellationToken);
            return donor is null ? Results.NotFound() : Results.Ok(donor);
        })
        .RequireAuthorization(AuthenticationExtensions.DonationsRead);

        donors.MapPost("/", async (
            CreateDonorRequest request,
            ICommandHandler<CreateDonorCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            var id = await handler.HandleAsync(request.ToCommand(), cancellationToken);
            return Results.Created($"/donors/{id}", null);
        })
        .AddEndpointFilter<ValidationFilter<CreateDonorRequest>>()
        .RequireAuthorization(AuthenticationExtensions.DonationsWrite);

        donors.MapPut("/{id:guid}", async (
            Guid id,
            UpdateDonorRequest request,
            ICommandHandler<UpdateDonorCommand, UpdateDonorOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id), cancellationToken);
            return outcome == UpdateDonorOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .AddEndpointFilter<ValidationFilter<UpdateDonorRequest>>()
        .RequireAuthorization(AuthenticationExtensions.DonationsWrite);

        donors.MapDelete("/{id:guid}", async (
            Guid id,
            ICommandHandler<EraseDonorCommand, EraseDonorOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new EraseDonorCommand(id), cancellationToken);
            return outcome == EraseDonorOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .RequireAuthorization(AuthenticationExtensions.DonorsErase);

        donors.MapGet("/{id:guid}/donations", async (
            Guid id,
            IQueryHandler<ListDonorDonationsQuery, IReadOnlyList<DonationReadModel>?> handler,
            CancellationToken cancellationToken,
            int? page,
            int? pageSize) =>
        {
            var donations = await handler.HandleAsync(
                new ListDonorDonationsQuery(id, page ?? 1, pageSize ?? 50), cancellationToken);
            return donations is null ? Results.NotFound() : Results.Ok(donations);
        })
        .RequireAuthorization(AuthenticationExtensions.DonationsRead);

        donors.MapGet("/{id:guid}/report", async (
            Guid id,
            IQueryHandler<GetDonorReportQuery, DonorReport?> handler,
            CancellationToken cancellationToken) =>
        {
            var report = await handler.HandleAsync(new GetDonorReportQuery(id), cancellationToken);
            return report is null ? Results.NotFound() : Results.Ok(report);
        })
        .RequireAuthorization(AuthenticationExtensions.DonationsRead);

        var donations = app.MapGroup("/donations").WithTags("Donations");

        donations.MapGet("/", async (
            IQueryHandler<ListDonationsQuery, IReadOnlyList<DonationReadModel>> handler,
            CancellationToken cancellationToken,
            int? page,
            int? pageSize) =>
            Results.Ok(await handler.HandleAsync(new ListDonationsQuery(null, page ?? 1, pageSize ?? 50), cancellationToken)))
        .RequireAuthorization(AuthenticationExtensions.DonationsRead);

        donations.MapGet("/{id:int}", async (
            int id,
            IQueryHandler<GetDonationByIdQuery, DonationReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var donation = await handler.HandleAsync(new GetDonationByIdQuery(id), cancellationToken);
            return donation is null ? Results.NotFound() : Results.Ok(donation);
        })
        .RequireAuthorization(AuthenticationExtensions.DonationsRead);

        donations.MapPost("/", async (
            CreateDonationRequest request,
            ICommandHandler<CreateDonationCommand, CreateDonationResult> handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

            return result.Outcome == CreateDonationOutcome.Created
                ? Results.Created($"/donations/{result.Id}", null)
                : Results.Problem(
                    type: "donor-not-found",
                    title: "There is no such donor.",
                    detail: "The donor named is not on file. Enter the donor first.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
        })
        .AddEndpointFilter<ValidationFilter<CreateDonationRequest>>()
        .RequireAuthorization(AuthenticationExtensions.DonationsWrite);

        donations.MapPut("/{id:int}", async (
            int id,
            UpdateDonationRequest request,
            ICommandHandler<UpdateDonationCommand, UpdateDonationOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id), cancellationToken);
            return outcome == UpdateDonationOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .AddEndpointFilter<ValidationFilter<UpdateDonationRequest>>()
        .RequireAuthorization(AuthenticationExtensions.DonationsWrite);

        donations.MapDelete("/{id:int}", async (
            int id,
            ICommandHandler<DeleteDonationCommand, DeleteDonationOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new DeleteDonationCommand(id), cancellationToken);

            return outcome switch
            {
                DeleteDonationOutcome.Deleted => Results.NoContent(),
                DeleteDonationOutcome.StillReferenced => Results.Problem(
                    detail: "Items packed in boxes still name this donation. Take them out of their boxes first.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.NotFound(),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.DonationsWrite);

        return app;
    }
}
