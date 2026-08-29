using System.Security.Claims;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Receivers;

namespace UA.Action.Freedom.Api.Receivers;

/// <summary>
/// Receivers — the destination of a box's contents in Ukraine — and, behind a separate policy,
/// their delivery detail.
/// </summary>
/// <remarks>
/// The split here is the most important authorization decision in the API.
/// <c>/receivers</c> returns reference, organisation and region: enough to plan a convoy and to
/// print on something that crosses a border. <c>/receivers/{ref}/detail</c> returns the street
/// address and the contact, and it is Ground Officer only — a manifest listing precise delivery
/// addresses is a targeting document (docs/domain/key-concepts.md § Data Sensitivity).
///
/// Three separate things enforce that, deliberately: the <c>receivers:detail</c> policy, a
/// distinct database identity, and a <c>DENY</c> on the sensitive schema that the application's
/// own identity cannot escape. Removing any one of them still leaves the address unreadable.
/// </remarks>
public static class ReceiverEndpoints
{
    public static WebApplication MapFreedomReceivers(this WebApplication app)
    {
        var receivers = app.MapGroup("/receivers").WithTags("Receivers");

        receivers.MapGet("/", async (
            IQueryHandler<ListReceiversQuery, IReadOnlyList<ReceiverReadModel>> handler,
            CancellationToken cancellationToken,
            int? page,
            int? pageSize) =>
        {
            var result = await handler.HandleAsync(new ListReceiversQuery(page ?? 1, pageSize ?? 50), cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization(AuthenticationExtensions.ReceiversRead);

        receivers.MapGet("/{receiverRef:guid}", async (
            Guid receiverRef,
            IQueryHandler<GetReceiverByRefQuery, ReceiverReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var receiver = await handler.HandleAsync(new GetReceiverByRefQuery(receiverRef), cancellationToken);
            return receiver is null ? Results.NotFound() : Results.Ok(receiver);
        })
        .RequireAuthorization(AuthenticationExtensions.ReceiversRead);

        receivers.MapPost("/", async (
            CreateReceiverRequest request,
            ICommandHandler<CreateReceiverCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            var receiverRef = await handler.HandleAsync(request.ToCommand(), cancellationToken);
            return Results.Created($"/receivers/{receiverRef}", null);
        })
        .AddEndpointFilter<ValidationFilter<CreateReceiverRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ReceiversWrite);

        receivers.MapPut("/{receiverRef:guid}", async (
            Guid receiverRef,
            UpdateReceiverRequest request,
            ICommandHandler<UpdateReceiverCommand, UpdateReceiverOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(receiverRef), cancellationToken);
            return outcome == UpdateReceiverOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .AddEndpointFilter<ValidationFilter<UpdateReceiverRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ReceiversWrite);

        receivers.MapDelete("/{receiverRef:guid}", async (
            Guid receiverRef,
            ICommandHandler<DeleteReceiverCommand, DeleteReceiverOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new DeleteReceiverCommand(receiverRef), cancellationToken);
            return outcome == DeleteReceiverOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        // Deleting a receiver also deletes its address, which needs the Ground Officer identity.
        .RequireAuthorization(AuthenticationExtensions.ReceiversDetail);

        receivers.MapGet("/{receiverRef:guid}/detail", async (
            Guid receiverRef,
            ClaimsPrincipal caller,
            IQueryHandler<GetReceiverDetailQuery, ReceiverDetailReadModel?> handler,
            CancellationToken cancellationToken,
            string? reason) =>
        {
            // Identity comes from the token, never from the request. An audit trail a caller
            // could write their own name into would not be one.
            var principalId = caller.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? caller.FindFirstValue("sub")
                              ?? "unknown";

            var detail = await handler.HandleAsync(
                new GetReceiverDetailQuery(receiverRef, principalId, reason), cancellationToken);

            return detail is null ? Results.NotFound() : Results.Ok(detail);
        })
        .RequireAuthorization(AuthenticationExtensions.ReceiversDetail);

        receivers.MapPut("/{receiverRef:guid}/detail", async (
            Guid receiverRef,
            SetReceiverDetailRequest request,
            ICommandHandler<SetReceiverDetailCommand, SetReceiverDetailOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(receiverRef), cancellationToken);

            return outcome == SetReceiverDetailOutcome.ReceiverNotFound
                ? Results.NotFound()
                : Results.NoContent();
        })
        .AddEndpointFilter<ValidationFilter<SetReceiverDetailRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ReceiversDetail);

        return app;
    }
}
