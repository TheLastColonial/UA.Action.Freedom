using Microsoft.Extensions.Options;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Api.Configuration.Scope;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Api.Receivers;
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

    private const string NoQrCodeProblem =
        "This box has no QR code. Issue one with POST /boxes/{id}/qr-code before printing a label.";

    private const string BayLocationMismatchProblem =
        "This bay does not belong to the location the box is currently at.";

    public static WebApplication MapFreedomBoxes(this WebApplication app)
    {
        var boxes = app.MapGroup("/boxes").WithTags("Boxes");

        boxes.MapGet("/", async (
            IQueryHandler<ListBoxesQuery, IReadOnlyList<BoxReadModel>> handler,
            IScopeGuard guard,
            CancellationToken cancellationToken,
            int? page,
            int? pageSize) =>
        {
            var visibility = await guard.LocationVisibilityAsync(cancellationToken);
            var result = await handler.HandleAsync(new ListBoxesQuery(page ?? 1, pageSize ?? 50, visibility), cancellationToken);
            return Results.Ok(result);
        })
        .ScopeExempt("a list: narrowed in the query by the caller's LocationVisibility")
        .RequireAuthorization(AuthenticationExtensions.BoxesRead);

        boxes.MapGet("/{id:int}", async (
            int id,
            IQueryHandler<GetBoxByIdQuery, BoxReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var box = await handler.HandleAsync(new GetBoxByIdQuery(id), cancellationToken);
            return box is null ? Results.NotFound() : Results.Ok(box);
        })
        .RequireBoxScope(BoxAccess.Read)
        .RequireAuthorization(AuthenticationExtensions.BoxesRead);

        boxes.MapPost("/", async (
            CreateBoxRequest request,
            IScopeGuard guard,
            ICommandHandler<CreateBoxCommand, CreateBoxResult> handler,
            CancellationToken cancellationToken) =>
        {
            // A scoped Loader may create a box at a location they manage, or an expected box with no location yet (O22).
            if (!await guard.CanAccessLocationAsync(request.LocationId, allowUnlocated: true, cancellationToken))
            {
                return await guard.RefusalAsync(cancellationToken);
            }

            var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

            return result.Outcome switch
            {
                CreateBoxOutcome.Created => Results.Created($"/boxes/{result.BoxId}", null),
                CreateBoxOutcome.ReceiverNotFound => ReceiverProblems.NotFound(),
                _ => ReceiverProblems.NotRegistered(),
            };
        })
        .ScopeExempt("the body locationId is checked in the route: assigned, or null for an expected box")
        .AddEndpointFilter<ValidationFilter<CreateBoxRequest>>()
        .RequireAuthorization(AuthenticationExtensions.BoxesWrite);

        boxes.MapPut("/{id:int}", async (
            int id,
            UpdateBoxRequest request,
            IScopeGuard guard,
            ICommandHandler<UpdateBoxCommand, UpdateBoxOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            // The source was checked by the filter (an unlocated box is the check-in). The destination must be a
            // location the caller manages: a scoped Loader cannot move a box out of reach, or to no location.
            if (!await guard.CanAccessLocationAsync(request.LocationId, allowUnlocated: false, cancellationToken))
            {
                return await guard.RefusalAsync(cancellationToken);
            }

            var outcome = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

            return outcome switch
            {
                UpdateBoxOutcome.Updated => Results.NoContent(),
                UpdateBoxOutcome.NotFound => Results.NotFound(),
                UpdateBoxOutcome.ReceiverNotFound => ReceiverProblems.NotFound(),
                UpdateBoxOutcome.ReceiverNotRegistered => ReceiverProblems.NotRegistered(),
                _ => Results.Problem(detail: ValidatedProblem, statusCode: StatusCodes.Status409Conflict),
            };
        })
        .RequireBoxScope(BoxAccess.Move)
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
        .RequireBoxScope(BoxAccess.Write)
        .RequireAuthorization(AuthenticationExtensions.BoxesWrite);

        boxes.MapPost("/{id:int}/validate", async (
            int id,
            ValidateBoxRequest request,
            ICurrentPerson currentPerson,
            ICommandHandler<ValidateBoxCommand, ValidateBoxOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            // The attestation names the caller's linked person. A login linked to no one cannot
            // vouch for a box.
            var (validatedBy, refusal) = await LoginNotLinked.RequireAsync(currentPerson, cancellationToken);
            if (refusal is not null)
            {
                return refusal;
            }

            var outcome = await handler.HandleAsync(request.ToCommand(id, validatedBy!.Value), cancellationToken);

            return outcome switch
            {
                ValidateBoxOutcome.Validated => Results.NoContent(),
                ValidateBoxOutcome.NotFound => Results.NotFound(),
                ValidateBoxOutcome.HasExpiredItems => Results.Problem(
                    type: "box-has-expired-items",
                    title: "This box holds an item that has expired.",
                    detail: "Take the expired item out of the box before it is validated.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.Problem(
                    detail: "This box has already been validated.",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .AddEndpointFilter<ValidationFilter<ValidateBoxRequest>>()
        .RequireBoxScope(BoxAccess.Write)
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
        .RequireBoxScope(BoxAccess.Read)
        .RequireAuthorization(AuthenticationExtensions.BoxesRead);

        boxes.MapPost("/{id:int}/items", async (
            int id,
            AddBoxItemRequest request,
            ICommandHandler<AddBoxItemCommand, AddBoxItemResult> handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

            return result.Outcome switch
            {
                AddBoxItemOutcome.Added => Results.Ok(new { result.ItemId, result.Warnings }),
                AddBoxItemOutcome.BoxNotFound => Results.NotFound(),
                AddBoxItemOutcome.CategoryNotFound => Results.Problem(
                    type: "category-not-found",
                    title: "There is no such category.",
                    detail: "The category named does not exist.",
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                AddBoxItemOutcome.DonationNotFound => Results.Problem(
                    type: "donation-not-found",
                    title: "There is no such donation.",
                    detail: "The donation named does not exist.",
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                _ => Results.Problem(detail: ValidatedProblem, statusCode: StatusCodes.Status409Conflict),
            };
        })
        .AddEndpointFilter<ValidationFilter<AddBoxItemRequest>>()
        .RequireBoxScope(BoxAccess.Write)
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
        .RequireBoxScope(BoxAccess.Write)
        .RequireAuthorization(AuthenticationExtensions.BoxesWrite);

        // QR labels. Issuing and revoking are ordinary box writes; reading, the image and the
        // printable label are ordinary box reads. A label is not box contents, so none of this
        // is refused on a validated box.
        boxes.MapPost("/{id:int}/qr-code", async (
            int id,
            ICommandHandler<IssueBoxQrCodeCommand, BoxQrCodeReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            // Re-issuing revokes whatever label the box had: the old token stops resolving here.
            var code = await handler.HandleAsync(new IssueBoxQrCodeCommand(id), cancellationToken);
            return code is null
                ? Results.NotFound()
                : Results.Created($"/boxes/scan/{code.Token}", null);
        })
        .RequireBoxScope(BoxAccess.Write)
        .RequireAuthorization(AuthenticationExtensions.BoxesWrite);

        boxes.MapGet("/{id:int}/qr-code", async (
            int id,
            IQueryHandler<GetBoxQrCodeQuery, BoxQrCodeReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var code = await handler.HandleAsync(new GetBoxQrCodeQuery(id), cancellationToken);
            return code is null ? Results.NotFound() : Results.Ok(code);
        })
        .RequireBoxScope(BoxAccess.Read)
        .RequireAuthorization(AuthenticationExtensions.BoxesRead);

        boxes.MapDelete("/{id:int}/qr-code", async (
            int id,
            ICommandHandler<RevokeBoxQrCodeCommand, RevokeBoxQrCodeOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new RevokeBoxQrCodeCommand(id), cancellationToken);
            return outcome == RevokeBoxQrCodeOutcome.Revoked ? Results.NoContent() : Results.NotFound();
        })
        .RequireBoxScope(BoxAccess.Write)
        .RequireAuthorization(AuthenticationExtensions.BoxesWrite);

        boxes.MapGet("/{id:int}/qr-code/image", async (
            int id,
            string? format,
            HttpContext http,
            IOptions<AppOptions> app,
            IQueryHandler<GetBoxQrCodeQuery, BoxQrCodeReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var code = await handler.HandleAsync(new GetBoxQrCodeQuery(id), cancellationToken);
            if (code is null)
            {
                return Results.NotFound();
            }

            var baseUrl = PublicBaseUrl(http, app.Value);
            return string.Equals(format, "png", StringComparison.OrdinalIgnoreCase)
                ? Results.Bytes(QrCodeRenderer.ToPng(code.Token, baseUrl), "image/png")
                : Results.Text(QrCodeRenderer.ToSvg(code.Token, baseUrl), "image/svg+xml");
        })
        .RequireBoxScope(BoxAccess.Read)
        .RequireAuthorization(AuthenticationExtensions.BoxesRead);

        // Bay allocation. Placing or moving a box is Loader-only — narrower than boxes:write —
        // because this is the on-site, physical act of shelving a box, not coordination
        // (docs/domain/key-concepts.md § Loader). Reading the current bay or its history is an
        // ordinary box read.
        boxes.MapPut("/{id:int}/bay", async (
            int id,
            AssignBoxBayRequest request,
            ICurrentPerson currentPerson,
            ICommandHandler<AssignBoxBayCommand, AssignBoxBayOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var (assignedBy, refusal) = await LoginNotLinked.RequireAsync(currentPerson, cancellationToken);
            if (refusal is not null)
            {
                return refusal;
            }

            var outcome = await handler.HandleAsync(request.ToCommand(id, assignedBy!.Value), cancellationToken);

            return outcome switch
            {
                AssignBoxBayOutcome.Assigned => Results.NoContent(),
                AssignBoxBayOutcome.BoxNotFound => Results.NotFound(),
                AssignBoxBayOutcome.BayNotFound => Results.Problem(
                    detail: "No such bay.", statusCode: StatusCodes.Status404NotFound),
                _ => Results.Problem(
                    detail: BayLocationMismatchProblem, statusCode: StatusCodes.Status409Conflict),
            };
        })
        .AddEndpointFilter<ValidationFilter<AssignBoxBayRequest>>()
        .RequireBoxScope(BoxAccess.Write)
        .RequireAuthorization(AuthenticationExtensions.BoxesAllocateBay);

        boxes.MapGet("/{id:int}/bay", async (
            int id,
            IQueryHandler<GetBoxBayQuery, BoxBayAssignmentReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var assignment = await handler.HandleAsync(new GetBoxBayQuery(id), cancellationToken);
            return assignment is null ? Results.NotFound() : Results.Ok(assignment);
        })
        .RequireBoxScope(BoxAccess.Read)
        .RequireAuthorization(AuthenticationExtensions.BoxesRead);

        boxes.MapDelete("/{id:int}/bay", async (
            int id,
            ICommandHandler<VacateBoxBayCommand, VacateBoxBayOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new VacateBoxBayCommand(id), cancellationToken);
            return outcome == VacateBoxBayOutcome.Vacated ? Results.NoContent() : Results.NotFound();
        })
        .RequireBoxScope(BoxAccess.Write)
        .RequireAuthorization(AuthenticationExtensions.BoxesAllocateBay);

        boxes.MapGet("/{id:int}/bay/history", async (
            int id,
            IQueryHandler<GetBoxBayHistoryQuery, IReadOnlyList<BoxBayAssignmentReadModel>> handler,
            CancellationToken cancellationToken) =>
        {
            var history = await handler.HandleAsync(new GetBoxBayHistoryQuery(id), cancellationToken);
            return Results.Ok(history);
        })
        .RequireBoxScope(BoxAccess.Read)
        .RequireAuthorization(AuthenticationExtensions.BoxesRead);

        boxes.MapGet("/{id:int}/label", async (
            int id,
            HttpContext http,
            IOptions<AppOptions> app,
            IQueryHandler<GetBoxByIdQuery, BoxReadModel?> boxHandler,
            IQueryHandler<GetBoxQrCodeQuery, BoxQrCodeReadModel?> codeHandler,
            CancellationToken cancellationToken) =>
        {
            var box = await boxHandler.HandleAsync(new GetBoxByIdQuery(id), cancellationToken);
            if (box is null)
            {
                return Results.NotFound();
            }

            var code = await codeHandler.HandleAsync(new GetBoxQrCodeQuery(id), cancellationToken);
            if (code is null)
            {
                return Results.Problem(detail: NoQrCodeProblem, statusCode: StatusCodes.Status409Conflict);
            }

            var svg = BoxLabelRenderer.ToSvg(box.Id, code.Token, code.IssuedAt, PublicBaseUrl(http, app.Value));
            return Results.Text(svg, "image/svg+xml");
        })
        .RequireBoxScope(BoxAccess.Read)
        .RequireAuthorization(AuthenticationExtensions.BoxesRead);

        boxes.MapGet("/scan/{token:guid}", async (
            Guid token,
            IScopeGuard guard,
            IQueryHandler<ResolveBoxByQrCodeQuery, BoxReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var box = await handler.HandleAsync(new ResolveBoxByQrCodeQuery(token), cancellationToken);

            // An out-of-scope box answers exactly as an unknown token does, so a label is not an oracle.
            return box is null || !await guard.CanAccessLocationAsync(box.LocationId, allowUnlocated: true, cancellationToken)
                ? Results.NotFound()
                : Results.Ok(box);
        })
        .ScopeExempt("scoped after the token resolves, answering 404 for an out-of-scope box")
        .RequireAuthorization(AuthenticationExtensions.BoxesRead);

        return app;
    }

    private static string PublicBaseUrl(HttpContext http, AppOptions app) =>
        string.IsNullOrWhiteSpace(app.PublicBaseUrl)
            ? $"{http.Request.Scheme}://{http.Request.Host}"
            : app.PublicBaseUrl;
}
