using FluentValidation;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Api.Manifests;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Declarations;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Api.Declarations;

/// <summary>Body of <c>POST .../declarations/{kind}/record</c>. <c>receiverRef</c> only for a goods list.</summary>
public sealed record RecordDeclarationRequest(string Reference, Guid? ReceiverRef = null);

public sealed class RecordDeclarationRequestValidator : AbstractValidator<RecordDeclarationRequest>
{
    public RecordDeclarationRequestValidator() => RuleFor(request => request.Reference).NotEmpty().MaximumLength(100);
}

/// <summary>Body of <c>POST .../declarations/{kind}/refused</c>: a bounded reason code, never free text.</summary>
public sealed record RefuseDeclarationRequest(string ReasonCode, Guid? ReceiverRef = null);

public sealed class RefuseDeclarationRequestValidator : AbstractValidator<RefuseDeclarationRequest>
{
    public RefuseDeclarationRequestValidator() => RuleFor(request => request.ReasonCode)
        .Must(DeclarationRefusalReasons.IsKnown)
        .WithMessage($"The reason must be one of: {string.Join(", ", DeclarationRefusalReasons.Codes)}. "
                     + "The authority's own text is never stored, because it can quote the declaration.");
}

/// <summary>
/// A vehicle's customs declarations (ADR 0005, ADR 0006): GMR, ENS, ELO and the Ukrainian goods lists,
/// on one lifecycle, under the truck-list entry they describe.
/// </summary>
/// <remarks>
/// Reading is <c>manifests:read</c> and every act is <c>manifests:declare</c> (Administrator or
/// Dispatcher). Filing is manual by default: <c>record</c> stores the reference a Dispatcher obtained in the
/// authority's portal, and <c>file</c> is only available for an authority whose submission mode is automatic.
/// </remarks>
public static class DeclarationEndpoints
{
    public static WebApplication MapFreedomDeclarations(this WebApplication app)
    {
        var declarations = app.MapGroup("/convoys/{id:int}/vehicles/{vin}/declarations").WithTags("Declarations");

        declarations.MapGet("/", async (
            int id,
            string vin,
            IQueryHandler<ListDeclarationsQuery, IReadOnlyList<DeclarationReadModel>?> handler,
            CancellationToken cancellationToken) =>
            await handler.HandleAsync(new ListDeclarationsQuery(id, vin), cancellationToken) is { } list
                ? Results.Ok(list)
                : Results.NotFound())
        .RequireAuthorization(AuthenticationExtensions.ManifestsRead);

        declarations.MapGet("/ens", async (
            int id,
            string vin,
            IQueryHandler<GetEnsDeclarationQuery, EnsDeclarationReadModel?> handler,
            CancellationToken cancellationToken) =>
            await handler.HandleAsync(new GetEnsDeclarationQuery(id, vin), cancellationToken) is { } ens
                ? Results.Ok(ens)
                : Results.NotFound())
        .RequireAuthorization(AuthenticationExtensions.ManifestsRead);

        // What would be filed, and what is still missing. Composed on the ordinary application
        // connection, so structurally incapable of carrying a Ukrainian delivery address: the Ground
        // Officer enters it in the portal themselves, and the sheet says so.
        declarations.MapGet("/ens/filing-sheet", async (
            int id,
            string vin,
            IQueryHandler<GetVehicleEnsFilingSheetQuery, EnsFilingSheetReadModel?> handler,
            CancellationToken cancellationToken) =>
            await handler.HandleAsync(new GetVehicleEnsFilingSheetQuery(id, vin), cancellationToken) is { } sheet
                ? Results.Ok(sheet)
                : Results.NotFound())
        .RequireAuthorization(AuthenticationExtensions.ManifestsRead);

        declarations.MapPut("/ens", async (
            int id,
            string vin,
            RecordEnsRequest request,
            ICommandHandler<RecordEnsDeclarationCommand, RecordEnsOutcome> handler,
            CancellationToken cancellationToken) =>
            RecordEnsResult(id, vin, await handler.HandleAsync(request.ToCommand(id, vin), cancellationToken)))
        .AddEndpointFilter<ValidationFilter<RecordEnsRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ManifestsDeclare);

        // Withdrawing a declaration because it was invalidated in ICS2 and will be refiled. Several ENS
        // fields are non-amendable, so invalidate-and-refile is the normal correction. The withdrawn MRN
        // is kept as history; only the vehicle's current declaration is cleared.
        declarations.MapDelete("/ens", async (
            int id,
            string vin,
            ICommandHandler<SupersedeEnsDeclarationCommand, SupersedeEnsOutcome> handler,
            CancellationToken cancellationToken) =>
            await handler.HandleAsync(new SupersedeEnsDeclarationCommand(id, vin), cancellationToken)
                == SupersedeEnsOutcome.Superseded
                ? Results.NoContent()
                : Results.NotFound())
        .RequireAuthorization(AuthenticationExtensions.ManifestsDeclare);

        // The declaration is written: the load it was written from is stored, and it is stale from then on
        // whenever the load differs (ADR 0005). A goods list names its receiver in the query.
        declarations.MapPost("/{kind}/ready", async (
            int id,
            string vin,
            string kind,
            Guid? receiverRef,
            ICommandHandler<MarkDeclarationReadyCommand, MarkDeclarationReadyOutcome> handler,
            CancellationToken cancellationToken) =>
            ParseKind(kind) is not { } parsed
                ? UnknownKind()
                : ReadyResult(await handler.HandleAsync(
                    new MarkDeclarationReadyCommand(id, vin, parsed, receiverRef), cancellationToken)))
        .RequireAuthorization(AuthenticationExtensions.ManifestsDeclare);

        declarations.MapPost("/{kind}/record", async (
            int id,
            string vin,
            string kind,
            RecordDeclarationRequest request,
            ICommandHandler<RecordDeclarationCommand, RecordDeclarationOutcome> handler,
            CancellationToken cancellationToken) =>
            ParseKind(kind) is not { } parsed
                ? UnknownKind()
                : RecordResult(await handler.HandleAsync(
                    new RecordDeclarationCommand(id, vin, parsed, request.Reference, request.ReceiverRef),
                    cancellationToken)))
        .AddEndpointFilter<ValidationFilter<RecordDeclarationRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ManifestsDeclare);

        declarations.MapPost("/{kind}/refused", async (
            int id,
            string vin,
            string kind,
            RefuseDeclarationRequest request,
            ICommandHandler<RefuseDeclarationCommand, RefuseDeclarationOutcome> handler,
            CancellationToken cancellationToken) =>
            ParseKind(kind) is not { } parsed
                ? UnknownKind()
                : RefuseResult(await handler.HandleAsync(
                    new RefuseDeclarationCommand(id, vin, parsed, request.ReasonCode, request.ReceiverRef),
                    cancellationToken)))
        .AddEndpointFilter<ValidationFilter<RefuseDeclarationRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ManifestsDeclare);

        declarations.MapPost("/{kind}/file", async (
            int id,
            string vin,
            string kind,
            ICommandHandler<FileDeclarationCommand, FileDeclarationOutcome> handler,
            CancellationToken cancellationToken) =>
            ParseKind(kind) is not { } parsed
                ? UnknownKind()
                : FileResult(await handler.HandleAsync(new FileDeclarationCommand(id, vin, parsed), cancellationToken)))
        .RequireAuthorization(AuthenticationExtensions.ManifestsDeclare);

        return app;
    }

    private static DeclarationKind? ParseKind(string kind) => kind.ToLowerInvariant() switch
    {
        "gmr" => DeclarationKind.Gmr,
        "ens" => DeclarationKind.Ens,
        "elo" => DeclarationKind.Elo,
        "goods-list" => DeclarationKind.GoodsList,
        _ => null,
    };

    private static IResult UnknownKind() => Results.Problem(
        detail: "The kind must be one of: gmr, ens, elo, goods-list.",
        statusCode: StatusCodes.Status400BadRequest);

    private static IResult RecordEnsResult(int id, string vin, RecordEnsOutcome outcome) => outcome switch
    {
        RecordEnsOutcome.Recorded => Results.Created($"/convoys/{id}/vehicles/{vin}/declarations/ens", null),
        RecordEnsOutcome.VehicleNotOnConvoy => Results.NotFound(),
        RecordEnsOutcome.AlreadyRecorded => Results.Problem(
            detail: "This vehicle already has an ICS2 declaration recorded. A declaration is write-once, "
                    + "because the logistics envelope names it: withdraw it with DELETE "
                    + "/convoys/{id}/vehicles/{vin}/declarations/ens first, which keeps it, and then record "
                    + "the refiled one.",
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Problem(
            detail: "That is not the shape of an ICS2 MRN. It is eighteen characters: two digits of year, "
                    + "the ISO alpha-2 code of the declaring country, then thirteen characters of reference "
                    + "and a check character, all upper case.",
            statusCode: StatusCodes.Status400BadRequest),
    };

    private static IResult RecordResult(RecordDeclarationOutcome outcome) => outcome switch
    {
        RecordDeclarationOutcome.Recorded => Results.NoContent(),
        RecordDeclarationOutcome.VehicleNotOnConvoy => Results.NotFound(),
        RecordDeclarationOutcome.AlreadyRecorded => Results.Problem(
            detail: "This declaration already carries a reference. A reference is write-once: the authority "
                    + "has been told it, so it is never overwritten.",
            statusCode: StatusCodes.Status409Conflict),
        RecordDeclarationOutcome.ReceiverRequired => Results.Problem(
            detail: "A Ukrainian goods list is one per receiver, so the receiver it was filed for is required.",
            statusCode: StatusCodes.Status400BadRequest),
        RecordDeclarationOutcome.ReceiverNotAllowed => Results.Problem(
            detail: "Only a Ukrainian goods list names a receiver.",
            statusCode: StatusCodes.Status400BadRequest),
        RecordDeclarationOutcome.UseEnsRoute => Results.Problem(
            detail: "An ENS carries who filed it and when it was accepted as well as its MRN. "
                    + "Use PUT /convoys/{id}/vehicles/{vin}/declarations/ens.",
            statusCode: StatusCodes.Status400BadRequest),
        _ => Results.Problem(
            detail: "The ELO is created from the ENS MRN, so this vehicle needs an accepted ENS before its "
                    + "envelope can be recorded.",
            statusCode: StatusCodes.Status409Conflict),
    };

    private static IResult ReadyResult(MarkDeclarationReadyOutcome outcome) => outcome switch
    {
        MarkDeclarationReadyOutcome.Ready => Results.NoContent(),
        MarkDeclarationReadyOutcome.VehicleNotOnConvoy => Results.NotFound(),
        MarkDeclarationReadyOutcome.ReceiverRequired => Results.Problem(
            detail: "A Ukrainian goods list is one per receiver, so the receiver it is for is required "
                    + "(?receiverRef=).",
            statusCode: StatusCodes.Status400BadRequest),
        MarkDeclarationReadyOutcome.ReceiverNotAllowed => Results.Problem(
            detail: "Only a Ukrainian goods list names a receiver.", statusCode: StatusCodes.Status400BadRequest),
        _ => Results.Problem(
            detail: "Only a draft declaration can be marked ready: this one has been filed.",
            statusCode: StatusCodes.Status409Conflict),
    };

    private static IResult RefuseResult(RefuseDeclarationOutcome outcome) => outcome switch
    {
        RefuseDeclarationOutcome.Refused => Results.NoContent(),
        RefuseDeclarationOutcome.NotFound => Results.NotFound(),
        RefuseDeclarationOutcome.UnknownReason => Results.Problem(
            detail: "That is not a known refusal reason.", statusCode: StatusCodes.Status400BadRequest),
        _ => Results.Problem(
            detail: "Only a filed declaration can be refused.", statusCode: StatusCodes.Status409Conflict),
    };

    private static IResult FileResult(FileDeclarationOutcome outcome) => outcome switch
    {
        FileDeclarationOutcome.Filed => Results.Accepted(),
        FileDeclarationOutcome.VehicleNotOnConvoy => Results.NotFound(),
        FileDeclarationOutcome.NotAutomatable => Results.Problem(
            detail: "Only the GMR and the ELO have a client. The ENS and the Ukrainian goods list are filed "
                    + "by hand: record the reference instead.",
            statusCode: StatusCodes.Status409Conflict),
        FileDeclarationOutcome.ManualMode => Results.Problem(
            detail: "This authority's submission mode is manual: record the reference instead, with "
                    + "POST .../declarations/{kind}/record.",
            statusCode: StatusCodes.Status409Conflict),
        FileDeclarationOutcome.ManifestNotApproved => Results.Problem(
            detail: "Declarations are prepared after sign-off: the manifest has to be approved first.",
            statusCode: StatusCodes.Status409Conflict),
        FileDeclarationOutcome.EnsNotAccepted => Results.Problem(
            detail: "The ELO is created from the ENS MRN, so this vehicle needs an accepted ENS first.",
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Problem(
            detail: "This declaration has already been filed.", statusCode: StatusCodes.Status409Conflict),
    };
}
