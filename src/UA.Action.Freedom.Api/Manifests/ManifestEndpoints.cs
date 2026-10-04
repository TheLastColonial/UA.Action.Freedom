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
/// <c>approve</c> is the one with consequences: it confirms the manifest, freezes it, and hands the
/// border paperwork off — the UK Goods Movement Reference, the document that travels with the
/// vehicle, and the French logistics envelope. Administrator only.
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

        // Cargo moved to the truck-list entry (ADR 0004, plan 07). These stay as a signpost until
        // plan 15 removes them, so a stale client is told where to go rather than getting a 404.
        manifests.MapPut("/{id}/boxes/{boxId:int}", (string id, int boxId) => BoxesMoved())
        .RequireAuthorization(AuthenticationExtensions.ManifestsWrite);

        manifests.MapDelete("/{id}/boxes/{boxId:int}", (string id, int boxId) => BoxesMoved())
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

        // The ICS2 Entry Summary Declaration. Recorded, not submitted: the Shared Trader Interface
        // speaks eDelivery AS4, and an always-on inbound access point is what recommendations §4.1
        // declines, so a Ground Officer files in the EU Customs Trader Portal and the MRN is recorded
        // here. Approving a manifest will not proceed without one (docs/adr/0003).
        manifests.MapGet("/{id}/ens", async (
            string id,
            IQueryHandler<GetManifestEnsQuery, EnsDeclarationReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var declaration = await handler.HandleAsync(new GetManifestEnsQuery(id), cancellationToken);
            return declaration is null ? Results.NotFound() : Results.Ok(declaration);
        })
        .RequireAuthorization(AuthenticationExtensions.ManifestsRead);

        // What would be filed, and what is still missing before it can be. Read-only and composed on
        // the ordinary application connection, so it is structurally incapable of carrying a Ukrainian
        // delivery address — the filer fetches that themselves from GET /receivers/{ref}/detail, under
        // the one policy that allows it, and the sheet says so rather than leaving them to wonder.
        manifests.MapGet("/{id}/ens/filing-sheet", async (
            string id,
            IQueryHandler<GetEnsFilingSheetQuery, EnsFilingSheetReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var sheet = await handler.HandleAsync(new GetEnsFilingSheetQuery(id), cancellationToken);
            return sheet is null ? Results.NotFound() : Results.Ok(sheet);
        })
        .RequireAuthorization(AuthenticationExtensions.ManifestsRead);

        manifests.MapPut("/{id}/ens", async (
            string id,
            RecordEnsRequest request,
            ICommandHandler<RecordEnsDeclarationCommand, RecordEnsOutcome> handler,
            CancellationToken cancellationToken) =>
            RecordEnsResult(id, await handler.HandleAsync(request.ToCommand(id), cancellationToken)))
        .AddEndpointFilter<ValidationFilter<RecordEnsRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ManifestsDeclare);

        // Withdrawing a declaration, because it was invalidated in ICS2 and will be refiled. Several
        // ENS fields are non-amendable — mode of transport, declarant, office of first entry, the
        // carrier identifier — so invalidate-and-refile is the normal correction path rather than an
        // exception. The withdrawn MRN is kept; only the manifest's current declaration is cleared.
        manifests.MapDelete("/{id}/ens", async (
            string id,
            ICommandHandler<SupersedeEnsDeclarationCommand, SupersedeEnsOutcome> handler,
            CancellationToken cancellationToken) =>
            SupersedeEnsResult(await handler.HandleAsync(
                new SupersedeEnsDeclarationCommand(id), cancellationToken)))
        .RequireAuthorization(AuthenticationExtensions.ManifestsDeclare);

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
        TransitionManifestOutcome.EnsNotFiled => Results.Problem(
            detail: "No ICS2 Entry Summary Declaration has been recorded for this manifest. France pairs the "
                    + "crossing against it at the Smart Border and will not issue a logistics envelope "
                    + "without one (ENV_CTR_RG08), so file the ENS and record its MRN with "
                    + "PUT /manifests/{id}/ens before approving.",
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Problem(
            detail: "A manifest cannot move to that state from the one it is in.",
            statusCode: StatusCodes.Status409Conflict),
    };

    private static IResult RecordEnsResult(string id, RecordEnsOutcome outcome) => outcome switch
    {
        RecordEnsOutcome.Recorded => Results.Created($"/manifests/{id}/ens", null),
        RecordEnsOutcome.ManifestNotFound => Results.NotFound(),
        RecordEnsOutcome.AlreadyRecorded => Results.Problem(
            detail: "This manifest already has an ICS2 declaration recorded. A declaration is write-once, "
                    + "because the logistics envelope names it: withdraw it with DELETE "
                    + "/manifests/{id}/ens first, which keeps it, and then record the refiled one.",
            statusCode: StatusCodes.Status409Conflict),
        RecordEnsOutcome.MalformedMrn => Results.Problem(
            detail: "That is not the shape of an ICS2 MRN. It is eighteen characters: two digits of year, "
                    + "the ISO alpha-2 code of the declaring country, then thirteen characters of reference "
                    + "and a check character, all upper case.",
            statusCode: StatusCodes.Status400BadRequest),
        _ => Frozen(),
    };

    private static IResult SupersedeEnsResult(SupersedeEnsOutcome outcome) => outcome switch
    {
        SupersedeEnsOutcome.Superseded => Results.NoContent(),
        SupersedeEnsOutcome.ManifestNotFound or SupersedeEnsOutcome.NotRecorded => Results.NotFound(),
        _ => Results.Problem(
            detail: "This manifest is frozen, and its logistics envelope already names this declaration. "
                    + "Withdrawing it now would leave French customs pairing the crossing against a "
                    + "formality that no longer exists.",
            statusCode: StatusCodes.Status409Conflict),
    };

    private static IResult BoxesMoved() => Results.Problem(
        detail: "A vehicle's cargo is no longer kept on its manifest. Use PUT or DELETE "
                + "/convoys/{id}/vehicles/{vin}/boxes/{boxId} on the truck-list entry instead.",
        statusCode: StatusCodes.Status410Gone);

    private static IResult Frozen() => Results.Problem(
        detail: "A Goods Movement Reference has been created for this manifest, so it can no longer be changed.",
        statusCode: StatusCodes.Status409Conflict);
}
