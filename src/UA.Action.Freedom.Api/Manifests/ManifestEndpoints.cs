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
/// Every state change a person can ask for is its own <c>POST</c> (<c>propose</c>, <c>approve</c>,
/// <c>reject</c>), one per edge of <c>docs/manifest-status.puml</c>, rather than a <c>PATCH</c> of a status
/// field: the truck-list precondition has nothing to do with the pair of states involved, and a status field
/// would make it look like data validation instead of a process. The remaining edge, Approved back to
/// Proposed, is taken only by a change to the vehicle's load.
///
/// <c>approve</c> signs the load off and hands no paperwork off: the GMR and the French envelope are filed
/// from the vehicle's declarations, and the travelling document is requested with <c>POST /{id}/document</c>.
/// Administrator only.
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

            return outcome == UpdateManifestOutcome.Updated ? Results.NoContent() : Results.NotFound();
        })
        .AddEndpointFilter<ValidationFilter<UpdateManifestRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ManifestsWrite);

        manifests.MapDelete("/{id}", async (
            string id,
            ICommandHandler<DeleteManifestCommand, DeleteManifestOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new DeleteManifestCommand(id), cancellationToken);

            return outcome == DeleteManifestOutcome.Deleted ? Results.NoContent() : Results.NotFound();
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

        manifests.MapGet("/{id}/weight", async (
            string id,
            IQueryHandler<GetManifestWeightQuery, ManifestWeightReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var weight = await handler.HandleAsync(new GetManifestWeightQuery(id), cancellationToken);
            return weight is null ? Results.NotFound() : Results.Ok(weight);
        })
        .RequireAuthorization(AuthenticationExtensions.ManifestsRead);

        // The French logistics envelope, as the Customs Worker obtained it. Read-only: there is no
        // POST here, because an envelope is requested by approving the manifest, the same way a GMR
        // is. A 404 means the worker has not got to it yet — or, if it stays a 404, that the
        // hand-off failed after the freeze and needs an operator (see ApproveManifestHandler).
        manifests.MapGet("/{id}/elo", async (
            string id,
            IQueryHandler<GetManifestEloQuery, EloEnvelopeReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var envelope = await handler.HandleAsync(new GetManifestEloQuery(id), cancellationToken);
            return envelope is null ? Results.NotFound() : Results.Ok(envelope);
        })
        .RequireAuthorization(AuthenticationExtensions.ManifestsRead);

        // The barcode itself, streamed through this authenticated endpoint rather than handed out as
        // a blob URL (docs/recommendations.md §4.3). It is the artifact a driver presents at the
        // Smart Border, and it is an ordinary manifest read: the document says nothing about the load
        // beyond what the customs declarations already do.
        manifests.MapGet("/{id}/elo/document", async (
            string id,
            IQueryHandler<GetManifestEloDocumentQuery, byte[]?> handler,
            CancellationToken cancellationToken) =>
        {
            var barcode = await handler.HandleAsync(new GetManifestEloDocumentQuery(id), cancellationToken);
            return barcode is null
                ? Results.NotFound()
                : Results.Bytes(barcode, "application/pdf", $"elo-{id}.pdf");
        })
        .RequireAuthorization(AuthenticationExtensions.ManifestsRead);

        // Approval signs off the load and nothing else (ADR 0004, ADR 0006): the GMR and the envelope are
        // filed afterwards from the vehicle's declarations. It stays the transition an Administrator alone
        // may make, because it signs the load off.
        manifests.MapPost("/{id}/approve", async (
            string id,
            ICommandHandler<ApproveManifestCommand, TransitionManifestOutcome> handler,
            CancellationToken cancellationToken) =>
            TransitionResult(await handler.HandleAsync(new ApproveManifestCommand(id), cancellationToken)))
        .RequireAuthorization(AuthenticationExtensions.ManifestsApprove);

        // The document that travels with the vehicle is requested explicitly, once the load is signed off.
        // It used to be a side effect of approval.
        manifests.MapPost("/{id}/document", async (
            string id,
            ICommandHandler<RequestManifestDocumentCommand, RequestManifestDocumentOutcome> handler,
            CancellationToken cancellationToken) =>
            await handler.HandleAsync(new RequestManifestDocumentCommand(id), cancellationToken) switch
            {
                RequestManifestDocumentOutcome.Requested => Results.Accepted(),
                RequestManifestDocumentOutcome.NotFound => Results.NotFound(),
                _ => Results.Problem(
                    detail: "The document describes a signed-off load, so the manifest has to be approved first.",
                    statusCode: StatusCodes.Status409Conflict),
            })
        .RequireAuthorization(AuthenticationExtensions.ManifestsWrite);

        // One route per edge a person can take.
        MapTransition(manifests, "propose", ManifestStatus.Proposed);
        MapTransition(manifests, "reject", ManifestStatus.Rejected);

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
        TransitionManifestOutcome.TruckListNotPublished => Results.Problem(
            detail: "This manifest's convoy has not published its truck list, so there is no fixed set of "
                    + "vehicles to propose against.",
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Problem(
            detail: "A manifest cannot move to that state from the one it is in.",
            statusCode: StatusCodes.Status409Conflict),
    };
}
