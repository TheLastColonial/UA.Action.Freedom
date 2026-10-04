using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Api.Receivers;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Api.Convoys;

/// <summary>
/// Convoys, their route, and their truck list. Reads are open to every operational role;
/// writes are Administrator and Dispatcher — planning a convoy and booking its crossings is
/// what the Dispatcher role exists for (docs/domain/key-concepts.md § Roles).
/// </summary>
/// <remarks>
/// Publishing the truck list is a <c>POST</c> to its own path rather than a field on
/// <c>PUT /convoys/{id}</c>. It is a one-way transition that closes the vehicle list, and
/// docs/process.puml puts it between convoy planning and manifest proposal; an ordinary update
/// able to set or clear it would route around that.
/// </remarks>
public static class ConvoyEndpoints
{
    /// <summary>Mirrors <c>dbo.ConvoyVehicle.WithdrawnReason</c>.</summary>
    private const int MaxWithdrawalReasonLength = 500;

    public static WebApplication MapFreedomConvoys(this WebApplication app)
    {
        var convoys = app.MapGroup("/convoys").WithTags("Convoys");

        convoys.MapGet("/", async (
            IQueryHandler<ListConvoysQuery, IReadOnlyList<ConvoyReadModel>> handler,
            CancellationToken cancellationToken,
            int? page,
            int? pageSize) =>
        {
            var result = await handler.HandleAsync(new ListConvoysQuery(page ?? 1, pageSize ?? 50), cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapGet("/{id:int}", async (
            int id,
            IQueryHandler<GetConvoyByIdQuery, ConvoyReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var convoy = await handler.HandleAsync(new GetConvoyByIdQuery(id), cancellationToken);
            return convoy is null ? Results.NotFound() : Results.Ok(convoy);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapPost("/", async (
            CreateConvoyRequest request,
            ICommandHandler<CreateConvoyCommand, int> handler,
            CancellationToken cancellationToken) =>
        {
            var id = await handler.HandleAsync(request.ToCommand(), cancellationToken);
            return Results.Created($"/convoys/{id}", null);
        })
        .AddEndpointFilter<ValidationFilter<CreateConvoyRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapPut("/{id:int}", async (
            int id,
            UpdateConvoyRequest request,
            ICommandHandler<UpdateConvoyCommand, UpdateConvoyOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id), cancellationToken);
            return outcome == UpdateConvoyOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .AddEndpointFilter<ValidationFilter<UpdateConvoyRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapDelete("/{id:int}", async (
            int id,
            ICommandHandler<DeleteConvoyCommand, DeleteConvoyOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new DeleteConvoyCommand(id), cancellationToken);
            return outcome switch
            {
                DeleteConvoyOutcome.Deleted => Results.NoContent(),
                DeleteConvoyOutcome.StillReferenced => Results.Problem(
                    detail: "This convoy has manifests, which are the record of its journey, so it cannot be removed.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.NotFound(),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapGet("/{id:int}/route", async (
            int id,
            IQueryHandler<GetConvoyRouteQuery, IReadOnlyList<RouteStopReadModel>?> handler,
            CancellationToken cancellationToken) =>
        {
            // Null means no such convoy; an empty list means a convoy whose route is not planned yet.
            var route = await handler.HandleAsync(new GetConvoyRouteQuery(id), cancellationToken);
            return route is null ? Results.NotFound() : Results.Ok(route);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapPut("/{id:int}/route", async (
            int id,
            ReplaceConvoyRouteRequest request,
            ICommandHandler<ReplaceConvoyRouteCommand, ReplaceConvoyRouteOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id), cancellationToken);
            return outcome == ReplaceConvoyRouteOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        })
        .AddEndpointFilter<ValidationFilter<ReplaceConvoyRouteRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapGet("/{id:int}/vehicles", async (
            int id,
            IQueryHandler<ListConvoyVehiclesQuery, IReadOnlyList<ConvoyVehicleReadModel>?> handler,
            CancellationToken cancellationToken) =>
        {
            var vehicles = await handler.HandleAsync(new ListConvoyVehiclesQuery(id), cancellationToken);
            return vehicles is null ? Results.NotFound() : Results.Ok(vehicles);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapPut("/{id:int}/vehicles/{vin}", async (
            int id,
            string vin,
            ICommandHandler<AssignVehicleToConvoyCommand, AssignVehicleOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new AssignVehicleToConvoyCommand(id, vin), cancellationToken);

            return outcome switch
            {
                AssignVehicleOutcome.Assigned => Results.NoContent(),
                AssignVehicleOutcome.ConvoyNotFound => Results.NotFound(),
                AssignVehicleOutcome.VehicleNotFound => Results.Problem(
                    detail: $"There is no vehicle with VIN '{vin}'.",
                    statusCode: StatusCodes.Status404NotFound),
                AssignVehicleOutcome.VehicleNotPassedInspection => Results.Problem(
                    detail: $"Vehicle '{vin}' has not passed its servicing inspection, so it cannot join a convoy.",
                    statusCode: StatusCodes.Status409Conflict),
                AssignVehicleOutcome.VehicleOnAnotherConvoy => Results.Problem(
                    detail: $"Vehicle '{vin}' is already on another convoy. Remove it from that convoy first.",
                    statusCode: StatusCodes.Status409Conflict),
                AssignVehicleOutcome.VehicleHandedOver => Results.Problem(
                    detail: $"Vehicle '{vin}' was handed over in Ukraine at the end of an earlier convoy.",
                    statusCode: StatusCodes.Status409Conflict),
                AssignVehicleOutcome.ConvoyArrived => ConvoyArrived(),
                _ => Results.Problem(
                    detail: "The truck list for this convoy has been published, so no more vehicles can join it.",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        // Before publication this takes the vehicle off the list; afterwards it records that the
        // vehicle left the convoy — a breakdown, most often — keeping its crew, its insurance and
        // its manifest, because the manifest still describes a load that is real.
        convoys.MapDelete("/{id:int}/vehicles/{vin}", async (
            int id,
            string vin,
            // A query parameter, not a body: DELETE bodies are inferred by nothing and dropped by
            // some proxies, and the reason is one short string.
            string? reason,
            ICommandHandler<UnassignVehicleFromConvoyCommand, UnassignVehicleOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            if (reason is { Length: > MaxWithdrawalReasonLength })
            {
                return Results.Problem(
                    detail: $"'reason' must be {MaxWithdrawalReasonLength} characters or fewer.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var outcome = await handler.HandleAsync(
                new UnassignVehicleFromConvoyCommand(id, vin, reason), cancellationToken);

            return outcome switch
            {
                UnassignVehicleOutcome.Unassigned or UnassignVehicleOutcome.Withdrawn => Results.NoContent(),
                UnassignVehicleOutcome.ConvoyNotFound => Results.NotFound(),
                UnassignVehicleOutcome.NotOnThisConvoy => Results.Problem(
                    detail: $"There is no vehicle with VIN '{vin}' on this convoy.",
                    statusCode: StatusCodes.Status404NotFound),
                UnassignVehicleOutcome.ConvoyArrived => ConvoyArrived(),
                _ => Results.Problem(
                    detail: $"Vehicle '{vin}' has already been withdrawn from this convoy.",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        // The Receiver this vehicle is handed over to in Ukraine. It has to be registered, which only an
        // Administrator can make it (ADR 0012); departure does not check it yet (plan 13).
        convoys.MapPut("/{id:int}/vehicles/{vin}/handover-receiver", async (
            int id,
            string vin,
            SetHandoverReceiverRequest request,
            ICommandHandler<SetHandoverReceiverCommand, SetHandoverReceiverOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id, vin), cancellationToken);

            return outcome switch
            {
                SetHandoverReceiverOutcome.Set => Results.NoContent(),
                SetHandoverReceiverOutcome.ConvoyNotFound => Results.NotFound(),
                SetHandoverReceiverOutcome.NotOnThisConvoy => Results.Problem(
                    detail: $"There is no vehicle with VIN '{vin}' on this convoy.",
                    statusCode: StatusCodes.Status404NotFound),
                SetHandoverReceiverOutcome.ConvoyArrived => ConvoyArrived(),
                SetHandoverReceiverOutcome.VehicleWithdrawn => Results.Problem(
                    detail: $"Vehicle '{vin}' has been withdrawn from this convoy.",
                    statusCode: StatusCodes.Status409Conflict),
                SetHandoverReceiverOutcome.ReceiverNotFound => ReceiverProblems.NotFound(),
                _ => ReceiverProblems.NotRegistered(),
            };
        })
        .AddEndpointFilter<ValidationFilter<SetHandoverReceiverRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        // A manifest is opened against a truck-list entry, which is why it is created here rather
        // than at POST /manifests: docs/process.puml orders the work Truck List Published, then
        // Manifest Proposed, and the manifest's (ConvoyId, Vin) is a foreign key to this entry
        // rather than two fields a caller can set to anything.
        convoys.MapPost("/{id:int}/vehicles/{vin}/manifest", async (
            int id,
            string vin,
            CreateConvoyVehicleManifestRequest request,
            ICommandHandler<CreateManifestCommand, CreateManifestOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request.ToCommand(id, vin), cancellationToken);

            return outcome switch
            {
                CreateManifestOutcome.Created => Results.Created($"/manifests/{request.Id}", null),
                CreateManifestOutcome.VehicleNotOnConvoy => Results.Problem(
                    detail: $"There is no vehicle with VIN '{vin}' on this convoy.",
                    statusCode: StatusCodes.Status404NotFound),
                CreateManifestOutcome.VehicleWithdrawn => Results.Problem(
                    detail: $"Vehicle '{vin}' has been withdrawn from this convoy.",
                    statusCode: StatusCodes.Status409Conflict),
                CreateManifestOutcome.AlreadyHasManifest => Results.Problem(
                    detail: $"Vehicle '{vin}' already has a manifest on this convoy.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.Problem(
                    detail: $"A manifest with reference '{request.Id}' already exists.",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .AddEndpointFilter<ValidationFilter<CreateConvoyVehicleManifestRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        // A vehicle's cargo is the boxes allocated to its truck-list entry (ADR 0004). A box is on at
        // most one vehicle, so PUT on a second vehicle moves it.
        convoys.MapGet("/{id:int}/vehicles/{vin}/boxes", async (
            int id,
            string vin,
            IQueryHandler<ListVehicleBoxesQuery, IReadOnlyList<ManifestBoxReadModel>?> handler,
            CancellationToken cancellationToken) =>
        {
            var boxes = await handler.HandleAsync(new ListVehicleBoxesQuery(id, vin), cancellationToken);
            return boxes is null ? Results.NotFound() : Results.Ok(boxes);
        })
        .RequireAuthorization(AuthenticationExtensions.BoxesRead);

        convoys.MapPut("/{id:int}/vehicles/{vin}/boxes/{boxId:int}", async (
            int id,
            string vin,
            int boxId,
            ICommandHandler<AllocateBoxCommand, AllocateBoxOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new AllocateBoxCommand(id, vin, boxId), cancellationToken);

            return outcome switch
            {
                AllocateBoxOutcome.Allocated or AllocateBoxOutcome.Moved or AllocateBoxOutcome.AlreadyAllocated =>
                    Results.NoContent(),
                AllocateBoxOutcome.VehicleNotOnConvoy => Results.Problem(
                    detail: $"There is no vehicle with VIN '{vin}' on this convoy.",
                    statusCode: StatusCodes.Status404NotFound),
                AllocateBoxOutcome.BoxNotFound => Results.Problem(
                    detail: "There is no box with that ID.",
                    statusCode: StatusCodes.Status404NotFound),
                AllocateBoxOutcome.VehicleWithdrawn => Results.Problem(
                    detail: $"Vehicle '{vin}' has been withdrawn from this convoy, so it takes no more cargo.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => LoadFrozen(),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.BoxesWrite);

        convoys.MapDelete("/{id:int}/vehicles/{vin}/boxes/{boxId:int}", async (
            int id,
            string vin,
            int boxId,
            ICommandHandler<RemoveBoxAllocationCommand, RemoveBoxAllocationOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new RemoveBoxAllocationCommand(id, vin, boxId), cancellationToken);

            return outcome switch
            {
                RemoveBoxAllocationOutcome.Removed => Results.NoContent(),
                RemoveBoxAllocationOutcome.VehicleNotOnConvoy => Results.Problem(
                    detail: $"There is no vehicle with VIN '{vin}' on this convoy.",
                    statusCode: StatusCodes.Status404NotFound),
                RemoveBoxAllocationOutcome.NotAllocated => Results.Problem(
                    detail: "That box is not on this vehicle.",
                    statusCode: StatusCodes.Status404NotFound),
                _ => LoadFrozen(),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.BoxesWrite);

        convoys.MapGet("/{id:int}/vehicles/{vin}/crew", async (
            int id,
            string vin,
            IQueryHandler<ListVehicleCrewQuery, IReadOnlyList<VehicleCrewReadModel>?> handler,
            CancellationToken cancellationToken) =>
        {
            var crew = await handler.HandleAsync(new ListVehicleCrewQuery(id, vin), cancellationToken);
            return crew is null ? Results.NotFound() : Results.Ok(crew);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapPut("/{id:int}/vehicles/{vin}/crew/{personId:guid}", async (
            int id,
            string vin,
            Guid personId,
            AssignCrewRequest request,
            ICommandHandler<AssignCrewToVehicleCommand, AssignCrewOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            // The role is optional and means Driver, which is what most crewing is.
            var outcome = await handler.HandleAsync(
                new AssignCrewToVehicleCommand(id, vin, personId, request.Role ?? CrewRole.Driver),
                cancellationToken);

            return outcome switch
            {
                AssignCrewOutcome.Assigned => Results.NoContent(),
                AssignCrewOutcome.ConvoyNotFound => Results.NotFound(),
                AssignCrewOutcome.VehicleNotFound => Results.Problem(
                    detail: $"There is no vehicle with VIN '{vin}' on this convoy.",
                    statusCode: StatusCodes.Status404NotFound),
                AssignCrewOutcome.PersonNotFound => Results.Problem(
                    detail: "There is no volunteer with that ID.",
                    statusCode: StatusCodes.Status404NotFound),
                AssignCrewOutcome.PersonNotADriver => Results.Problem(
                    detail: "That volunteer is not registered as a driver. They can ride as a passenger instead.",
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                AssignCrewOutcome.ConvoyArrived => ConvoyArrived(),
                AssignCrewOutcome.VehicleWithdrawn => Results.Problem(
                    detail: $"Vehicle '{vin}' has been withdrawn from this convoy, so its crew can no longer change.",
                    statusCode: StatusCodes.Status409Conflict),
                AssignCrewOutcome.OnAnotherVehicle => Results.Problem(
                    detail: "That volunteer is already crewing another vehicle on this convoy. A person takes one seat per convoy.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.Problem(
                    detail: "That volunteer is already crewing this vehicle.",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .AddEndpointFilter<ValidationFilter<AssignCrewRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysAssignDrivers);

        convoys.MapDelete("/{id:int}/vehicles/{vin}/crew/{personId:guid}", async (
            int id,
            string vin,
            Guid personId,
            ICommandHandler<UnassignCrewFromVehicleCommand, UnassignCrewOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(
                new UnassignCrewFromVehicleCommand(id, vin, personId), cancellationToken);

            return outcome switch
            {
                UnassignCrewOutcome.Unassigned => Results.NoContent(),
                UnassignCrewOutcome.ConvoyNotFound => Results.NotFound(),
                UnassignCrewOutcome.ConvoyArrived => ConvoyArrived(),
                UnassignCrewOutcome.NotOnThisConvoy => Results.Problem(
                    detail: $"There is no vehicle with VIN '{vin}' on this convoy.",
                    statusCode: StatusCodes.Status404NotFound),
                _ => Results.NotFound(),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysAssignDrivers);

        convoys.MapGet("/{id:int}/vehicles/{vin}/insurance", async (
            int id,
            string vin,
            IQueryHandler<GetInsuranceQuery, VehicleInsuranceReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            var policy = await handler.HandleAsync(new GetInsuranceQuery(id, vin), cancellationToken);
            return policy is null ? Results.NotFound() : Results.Ok(policy);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapPut("/{id:int}/vehicles/{vin}/insurance", async (
            int id,
            string vin,
            RecordInsuranceRequest request,
            ICurrentPerson currentPerson,
            ICommandHandler<RecordInsuranceCommand, RecordInsuranceOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            // Who recorded the policy comes from the login, never the body.
            var (recordedBy, refusal) = await LoginNotLinked.RequireAsync(currentPerson, cancellationToken);
            if (refusal is not null)
            {
                return refusal;
            }

            var outcome = await handler.HandleAsync(request.ToCommand(id, vin, recordedBy!.Value), cancellationToken);

            return outcome switch
            {
                RecordInsuranceOutcome.Recorded => Results.NoContent(),
                RecordInsuranceOutcome.ConvoyNotFound => Results.NotFound(),
                RecordInsuranceOutcome.ConvoyArrived => ConvoyArrived(),
                _ => Results.Problem(
                    detail: $"There is no vehicle with VIN '{vin}' on this convoy.",
                    statusCode: StatusCodes.Status404NotFound),
            };
        })
        .AddEndpointFilter<ValidationFilter<RecordInsuranceRequest>>()
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapDelete("/{id:int}/vehicles/{vin}/insurance", async (
            int id,
            string vin,
            ICommandHandler<RemoveInsuranceCommand, RemoveInsuranceOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new RemoveInsuranceCommand(id, vin), cancellationToken);

            return outcome switch
            {
                RemoveInsuranceOutcome.Removed => Results.NoContent(),
                RemoveInsuranceOutcome.ConvoyArrived => ConvoyArrived(),
                _ => Results.NotFound(),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapGet("/{id:int}/readiness", async (
            int id,
            IQueryHandler<GetConvoyReadinessQuery, ConvoyReadinessReadModel?> handler,
            CancellationToken cancellationToken) =>
        {
            // Advisory: it says what is missing, it blocks nothing.
            var readiness = await handler.HandleAsync(new GetConvoyReadinessQuery(id), cancellationToken);
            return readiness is null ? Results.NotFound() : Results.Ok(readiness);
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysRead);

        convoys.MapPost("/{id:int}/arrive", async (
            int id,
            ICommandHandler<ArriveConvoyCommand, ArriveConvoyResult> handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new ArriveConvoyCommand(id), cancellationToken);

            return result.Outcome switch
            {
                ArriveConvoyOutcome.Arrived => Results.NoContent(),
                ArriveConvoyOutcome.NotFound => Results.NotFound(),
                ArriveConvoyOutcome.TruckListNotPublished => Results.Problem(
                    detail: "This convoy's truck list was never published, so it has not travelled.",
                    statusCode: StatusCodes.Status409Conflict),
                ArriveConvoyOutcome.AlreadyArrived => Results.Problem(
                    detail: "This convoy has already arrived.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.Problem(
                    detail: "These vehicles have no Delivered, Lost or Returned manifest yet: "
                            + string.Join(", ", result.StillTravelling) + ".",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        convoys.MapPost("/{id:int}/publish-truck-list", async (
            int id,
            ICommandHandler<PublishTruckListCommand, PublishTruckListOutcome> handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(new PublishTruckListCommand(id), cancellationToken);

            return outcome switch
            {
                PublishTruckListOutcome.Published => Results.NoContent(),
                PublishTruckListOutcome.NotFound => Results.NotFound(),
                _ => Results.Problem(
                    detail: "The truck list for this convoy has already been published.",
                    statusCode: StatusCodes.Status409Conflict),
            };
        })
        .RequireAuthorization(AuthenticationExtensions.ConvoysWrite);

        return app;
    }

    private static IResult LoadFrozen() => Results.Problem(
        detail: "A Goods Movement Reference has been created for this vehicle's manifest, so its load can no longer change.",
        statusCode: StatusCodes.Status409Conflict);

    private static IResult ConvoyArrived() => Results.Problem(
        detail: "This convoy has arrived. Its crew and insurance are a record of the journey and can no longer change.",
        statusCode: StatusCodes.Status409Conflict);
}
