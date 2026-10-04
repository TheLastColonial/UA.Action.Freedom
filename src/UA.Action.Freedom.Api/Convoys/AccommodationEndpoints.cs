using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Api.Convoys;

/// <summary>
/// A convoy's accommodation (P2, P8, P13, P16, O4, O30): bookings at route points, the crew they cover, the nights a
/// crew member arranges themselves, and the coverage grid. Reads are <c>convoys:read</c>, writes
/// <c>convoys:write</c>. Coverage is advice until plan 13 makes it a requirement.
/// </summary>
public static class AccommodationEndpoints
{
    public static WebApplication MapFreedomAccommodation(this WebApplication app)
    {
        var accommodation = app.MapGroup("/convoys/{id:int}/accommodation").WithTags("Accommodation");

        accommodation.MapGet("", async (
            int id,
            IQueryHandler<GetAccommodationQuery, AccommodationReadModel?> handler,
            CancellationToken cancellationToken) =>
            await handler.HandleAsync(new GetAccommodationQuery(id), cancellationToken) is { } result
                ? Results.Ok(result)
                : Results.NotFound())
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        accommodation.MapGet("/coverage", async (
            int id,
            IQueryHandler<GetAccommodationCoverageQuery, AccommodationCoverageReadModel?> handler,
            CancellationToken cancellationToken) =>
            await handler.HandleAsync(new GetAccommodationCoverageQuery(id), cancellationToken) is { } result
                ? Results.Ok(result)
                : Results.NotFound())
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        accommodation.MapPost("", async (
            int id,
            AccommodationBookingRequest request,
            ICommandHandler<BookAccommodationCommand, BookAccommodationResult> handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new BookAccommodationCommand(request.ToRecord(id)), cancellationToken);

            return result.Outcome switch
            {
                BookAccommodationOutcome.Booked =>
                    Results.Created($"/convoys/{id}/accommodation/{result.Id}", new { id = result.Id }),
                BookAccommodationOutcome.ConvoyNotFound => Results.NotFound(),
                BookAccommodationOutcome.ConvoyArrived => ConvoyArrived(),
                BookAccommodationOutcome.PointNotOnConvoy => PointNotOnConvoy(),
                _ => GuestNotOnCrew(),
            };
        })
        .AddEndpointFilter<ValidationFilter<AccommodationBookingRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        accommodation.MapPut("/{bookingId:int}", async (
            int id,
            int bookingId,
            AccommodationBookingRequest request,
            ICommandHandler<ReplaceAccommodationCommand, ReplaceAccommodationOutcome> handler,
            CancellationToken cancellationToken) =>
            await handler.HandleAsync(new ReplaceAccommodationCommand(bookingId, request.ToRecord(id)), cancellationToken) switch
            {
                ReplaceAccommodationOutcome.Replaced => Results.NoContent(),
                ReplaceAccommodationOutcome.ConvoyNotFound or ReplaceAccommodationOutcome.NotFound => Results.NotFound(),
                ReplaceAccommodationOutcome.ConvoyArrived => ConvoyArrived(),
                ReplaceAccommodationOutcome.Cancelled => BookingCancelled(),
                ReplaceAccommodationOutcome.PointNotOnConvoy => PointNotOnConvoy(),
                _ => GuestNotOnCrew(),
            })
        .AddEndpointFilter<ValidationFilter<AccommodationBookingRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        // Cancelling is a stamp, not a delete: the booking and its cost stay on record.
        accommodation.MapDelete("/{bookingId:int}", async (
            int id,
            int bookingId,
            ICommandHandler<CancelAccommodationCommand, CancelAccommodationOutcome> handler,
            CancellationToken cancellationToken) =>
            await handler.HandleAsync(new CancelAccommodationCommand(id, bookingId), cancellationToken) switch
            {
                CancelAccommodationOutcome.Cancelled => Results.NoContent(),
                _ => Results.NotFound(),
            })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        accommodation.MapPost("/{bookingId:int}/migrate", async (
            int id,
            int bookingId,
            MigrateAccommodationRequest request,
            ICommandHandler<MigrateAccommodationCommand, MigrateAccommodationOutcome> handler,
            CancellationToken cancellationToken) =>
            await handler.HandleAsync(
                new MigrateAccommodationCommand(id, bookingId, request.FromPersonId, request.ToPersonId), cancellationToken) switch
            {
                MigrateAccommodationOutcome.Migrated => Results.NoContent(),
                MigrateAccommodationOutcome.ConvoyNotFound or MigrateAccommodationOutcome.NotFound => Results.NotFound(),
                MigrateAccommodationOutcome.ConvoyArrived => ConvoyArrived(),
                MigrateAccommodationOutcome.Cancelled => BookingCancelled(),
                MigrateAccommodationOutcome.NotAGuest => Results.Problem(
                    detail: "That person is not a guest on this booking.",
                    type: "accommodation-not-a-guest",
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                _ => Results.Problem(
                    detail: "The replacement must be crewed on this convoy.",
                    type: "accommodation-replacement-not-on-crew",
                    statusCode: StatusCodes.Status422UnprocessableEntity),
            })
        .AddEndpointFilter<ValidationFilter<MigrateAccommodationRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        // A crew member arranging their own stay at an overnight stop (O4, O30).
        accommodation.MapPut("/self/{routePointId:int}/{personId:guid}", async (
            int id,
            int routePointId,
            Guid personId,
            ICommandHandler<SetSelfAccommodationCommand, SetSelfAccommodationOutcome> handler,
            CancellationToken cancellationToken) =>
            await handler.HandleAsync(new SetSelfAccommodationCommand(id, routePointId, personId), cancellationToken) switch
            {
                SetSelfAccommodationOutcome.Set => Results.NoContent(),
                SetSelfAccommodationOutcome.ConvoyNotFound => Results.NotFound(),
                SetSelfAccommodationOutcome.ConvoyArrived => ConvoyArrived(),
                SetSelfAccommodationOutcome.PointNotOnConvoy => PointNotOnConvoy(),
                SetSelfAccommodationOutcome.NotOvernight => Results.Problem(
                    detail: "Only an overnight stop needs accommodation, so only one can be flagged.",
                    type: "route-point-not-overnight",
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                _ => GuestNotOnCrew(),
            })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        accommodation.MapDelete("/self/{routePointId:int}/{personId:guid}", async (
            int id,
            int routePointId,
            Guid personId,
            ICommandHandler<ClearSelfAccommodationCommand, ClearSelfAccommodationOutcome> handler,
            CancellationToken cancellationToken) =>
            await handler.HandleAsync(new ClearSelfAccommodationCommand(id, routePointId, personId), cancellationToken) switch
            {
                ClearSelfAccommodationOutcome.Cleared => Results.NoContent(),
                _ => Results.NotFound(),
            })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        return app;
    }

    private static IResult ConvoyArrived() => Results.Problem(
        detail: "This convoy has arrived, so its accommodation can no longer change.",
        statusCode: StatusCodes.Status409Conflict);

    private static IResult BookingCancelled() => Results.Problem(
        detail: "This booking has been cancelled.",
        type: "accommodation-cancelled",
        statusCode: StatusCodes.Status409Conflict);

    private static IResult PointNotOnConvoy() => Results.Problem(
        detail: "That route point is not on this convoy's route.",
        type: "route-point-unknown",
        statusCode: StatusCodes.Status422UnprocessableEntity);

    private static IResult GuestNotOnCrew() => Results.Problem(
        detail: "Everyone named must be crewed on this convoy.",
        type: "accommodation-guest-not-on-crew",
        statusCode: StatusCodes.Status422UnprocessableEntity);
}
