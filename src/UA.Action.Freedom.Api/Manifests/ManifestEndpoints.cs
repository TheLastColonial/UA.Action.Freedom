using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Api.Manifests;

/// <summary>
/// Manifests: the central document of the system, and the lifecycle it moves through.
/// </summary>
/// <remarks>
/// Every state change is its own <c>POST</c>, one per edge of <c>docs/manifest-status.puml</c>,
/// rather than a <c>PATCH</c> of a status field. The happy path is linear, most pairs of states
/// are not connected, and two of the rules — the truck-list precondition and the GMR freeze —
/// have nothing to do with the pair of states involved. A status field would make all of that
/// look like data validation instead of a process.
///
/// <c>approve</c> is the one with consequences: it confirms the manifest, freezes it, and hands
/// the Goods Movement Reference to the customs worker. Administrator only.
/// </remarks>
public static class ManifestEndpoints
{
    public static WebApplication MapFreedomManifests(this WebApplication app)
    {
        var manifests = app.MapGroup("/manifests").WithTags("Manifests");

        manifests.MapGet("/", async (
            IQueryHandler<ListManifestsQuery, IReadOnlyList<ManifestReadModel>> handler,
            CancellationToken cancellationToken,
            int? page,
            int? pageSize) =>
        {
            var result = await handler.HandleAsync(new ListManifestsQuery(page ?? 1, pageSize ?? 50), cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization(AuthenticationExtensions.ManifestsRead);

        manifests.MapGet("/{id}", async (
            string id,
            IQueryHandler<GetManifestByIdQuery, ManifestReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var manifest = await handler.HandleAsync(new GetManifestByIdQuery(id), cancellationToken);
            return manifest is null ? Results.NotFound() : Results.Ok(manifest);
        })
        .RequireAuthorization(AuthenticationExtensions.ManifestsRead);

        // There is no POST /manifests. A manifest is the paperwork for one vehicle on one convoy,
        // so it is opened against that truck-list entry:
        // POST /convoys/{id}/vehicles/{vin}/manifest.

        manifests.MapPut("/{id}", async (
            string id,
            UpdateManifestRequest request,
            ICommandHandler<UpdateManifestCommand, UpdateManifestOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

            return outcome switch
            {
                UpdateManifestOutcome.Updated => Results.NoContent(),
                UpdateManifestOutcome.NotFound => Results.NotFound(),
                _ => Frozen(),
            };
        })
        .AddEndpointFilter<ValidationFilter<UpdateManifestRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ManifestsWrite);

        manifests.MapDelete("/{id}", async (
            string id,
            ICommandHandler<DeleteManifestCommand, DeleteManifestOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new DeleteManifestCommand(id), cancellationToken);

            return outcome switch
            {
                DeleteManifestOutcome.Deleted => Results.NoContent(),
                DeleteManifestOutcome.NotFound => Results.NotFound(),
                _ => Frozen(),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.ManifestsWrite);

        // A read. Crewing happens once, on the truck-list entry
        // (PUT /convoys/{id}/vehicles/{vin}/crew/{personId}); this reports who is travelling with
        // this manifest's vehicle. There used to be a PUT here writing a second, unconnected crew
        // record, which is how a printed manifest could name people the insurance had never heard
        // of.
        manifests.MapGet("/{id}/crew", async (
            string id,
            IQueryHandler<ListManifestCrewQuery, IReadOnlyList<VehicleCrewReadModel>?> handler,
            CancellationToken cancellationToken) =>
        {
            var crew = await handler.HandleAsync(new ListManifestCrewQuery(id), cancellationToken);
            return crew is null ? Results.NotFound() : Results.Ok(crew);
        })
        .RequireAuthorization(AuthenticationExtensions.ManifestsRead);

        manifests.MapGet("/{id}/boxes", async (
            string id,
            IQueryHandler<ListManifestBoxesQuery, IReadOnlyList<ManifestBoxReadModel>?> handler,
            CancellationToken cancellationToken) =>
        {
            var boxes = await handler.HandleAsync(new ListManifestBoxesQuery(id), cancellationToken);
            return boxes is null ? Results.NotFound() : Results.Ok(boxes);
        })
        .RequireAuthorization(AuthenticationExtensions.ManifestsRead);

        manifests.MapPut("/{id}/boxes/{boxId:int}", async (
            string id,
            int boxId,
            ICommandHandler<AddManifestBoxCommand, ManifestBoxOutcome> handler,
            CancellationToken cancellationToken) =>
            BoxResult(await handler.HandleAsync(new AddManifestBoxCommand(id, boxId), cancellationToken)))
        .RequireAuthorization(AuthenticationExtensions.ManifestsWrite);

        manifests.MapDelete("/{id}/boxes/{boxId:int}", async (
            string id,
            int boxId,
            ICommandHandler<RemoveManifestBoxCommand, ManifestBoxOutcome> handler,
            CancellationToken cancellationToken) =>
            BoxResult(await handler.HandleAsync(new RemoveManifestBoxCommand(id, boxId), cancellationToken)))
        .RequireAuthorization(AuthenticationExtensions.ManifestsWrite);

        manifests.MapGet("/{id}/weight", async (
            string id,
            IQueryHandler<GetManifestWeightQuery, ManifestWeightReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var weight = await handler.HandleAsync(new GetManifestWeightQuery(id), cancellationToken);
            return weight is null ? Results.NotFound() : Results.Ok(weight);
        })
        .RequireAuthorization(AuthenticationExtensions.ManifestsRead);

        // Approval is the fork in docs/process.puml and the point of no return, so it is the one
        // transition an Administrator alone may make.
        manifests.MapPost("/{id}/approve", async (
            string id,
            ICommandHandler<ApproveManifestCommand, TransitionManifestOutcome> handler,
            CancellationToken cancellationToken) =>
            TransitionResult(await handler.HandleAsync(new ApproveManifestCommand(id), cancellationToken)))
        .RequireAuthorization(AuthenticationExtensions.ManifestsApprove);

        // One route per edge of the diagram.
        MapTransition(manifests, "propose", ManifestStatus.Proposed);
        MapTransition(manifests, "reject", ManifestStatus.Rejected);
        MapTransition(manifests, "prepare", ManifestStatus.Preparing);
        MapTransition(manifests, "ready", ManifestStatus.Ready);
        MapTransition(manifests, "depart", ManifestStatus.InTransit);
        MapTransition(manifests, "deliver", ManifestStatus.Delivered);
        MapTransition(manifests, "lose", ManifestStatus.Lost);
        MapTransition(manifests, "return", ManifestStatus.Returned);

        return app;
    }

    private static void MapTransition(RouteGroupBuilder manifests, string route, ManifestStatus to) =>
        manifests.MapPost($"/{{id}}/{route}", async (
            string id,
            ICommandHandler<TransitionManifestCommand, TransitionManifestOutcome> handler,
            CancellationToken cancellationToken) =>
            TransitionResult(await handler.HandleAsync(new TransitionManifestCommand(id, to), cancellationToken)))
        .RequireAuthorization(AuthenticationExtensions.ManifestsWrite);

    private static IResult TransitionResult(TransitionManifestOutcome outcome) => outcome switch
    {
        TransitionManifestOutcome.Transitioned => Results.NoContent(),
        TransitionManifestOutcome.NotFound => Results.NotFound(),
        TransitionManifestOutcome.Frozen => Frozen(),
        TransitionManifestOutcome.TruckListNotPublished => Results.Problem(
            detail: "This manifest's convoy has not published its truck list, so there is no fixed set of "
                    + "vehicles to propose against.",
            statusCode: StatusCodes.Status409Conflict),
        TransitionManifestOutcome.NotInsured => Results.Problem(
            detail: "This vehicle cannot depart: its insurance is not recorded, was voided by a crew change, "
                    + "or does not cover today. Record the insurance for its current crew first.",
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Problem(
            detail: "A manifest cannot move to that state from the one it is in.",
            statusCode: StatusCodes.Status409Conflict),
    };

    private static IResult BoxResult(ManifestBoxOutcome outcome) => outcome switch
    {
        ManifestBoxOutcome.Changed => Results.NoContent(),
        ManifestBoxOutcome.ManifestNotFound or ManifestBoxOutcome.BoxNotFound => Results.NotFound(),
        _ => Frozen(),
    };

    private static IResult Frozen() => Results.Problem(
        detail: "A Goods Movement Reference has been created for this manifest, so it can no longer be changed.",
        statusCode: StatusCodes.Status409Conflict);
}
