using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Manifests;

/// <summary>
/// Open the document pack for one vehicle on one convoy.
/// </summary>
/// <remarks>
/// A manifest is opened <em>against a truck-list entry</em>, which is why the convoy and the
/// vehicle come from the route rather than the body: <c>POST /convoys/{id}/vehicles/{vin}/manifest</c>.
/// docs/process.puml has always ordered the work that way — Truck List Created, Truck List
/// Published, Manifest Proposed — but the pair used to be two optional fields anyone could set to
/// anything, so a manifest could name a truck that was on a different convoy, or none.
///
/// <para>
/// The reference is still supplied by the caller. It is a document number people read out at a
/// border, so it is a natural key like a VIN rather than a minted identifier.
/// </para>
/// </remarks>
public sealed record CreateManifestCommand(
    string Id, int ConvoyId, string Vin, string? DeliveryNotes, bool FerryBookingComplete);

public enum CreateManifestOutcome
{
    Created,

    /// <summary>A manifest with that reference already exists.</summary>
    Conflict,

    /// <summary>That vehicle is not on that convoy's truck list.</summary>
    VehicleNotOnConvoy,

    /// <summary>It left the convoy, so there is nothing for it to carry across a border on this journey.</summary>
    VehicleWithdrawn,

    /// <summary>This vehicle already has a manifest for this convoy.</summary>
    AlreadyHasManifest
}

public sealed class CreateManifestHandler(IManifestRepository repository, IConvoyVehicleRepository truckList)
    : ICommandHandler<CreateManifestCommand, CreateManifestOutcome>
{
    public async Task<CreateManifestOutcome> HandleAsync(
        CreateManifestCommand command, CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(command.Id, cancellationToken))
        {
            return CreateManifestOutcome.Conflict;
        }

        var entry = await truckList.GetAsync(command.ConvoyId, command.Vin, cancellationToken);

        if (entry is null)
        {
            return CreateManifestOutcome.VehicleNotOnConvoy;
        }

        if (entry.Withdrawn)
        {
            return CreateManifestOutcome.VehicleWithdrawn;
        }

        // One manifest per vehicle per convoy. The unique constraint is the real guard; asking
        // first is what turns a foreign-key exception into a 409 the caller can act on.
        if (await repository.GetForVehicleAsync(command.ConvoyId, command.Vin, cancellationToken) is not null)
        {
            return CreateManifestOutcome.AlreadyHasManifest;
        }

        // A manifest is Created before it is populated — no teams, no cargo required yet. That is
        // the first state of the diagram, and proposing it is what asserts it is complete enough
        // to look at.
        await repository.AddAsync(
            new ManifestReadModel(
                command.Id,
                command.ConvoyId,
                command.Vin,
                ManifestStatus.Created,
                command.DeliveryNotes,
                command.FerryBookingComplete,
                GmrSubmittedAt: null),
            cancellationToken);

        return CreateManifestOutcome.Created;
    }
}

/// <summary>
/// Change the notes or the ferry booking on a manifest.
/// </summary>
/// <remarks>
/// The convoy and the vehicle are deliberately absent. They are the manifest's identity — the
/// truck-list entry it is the paperwork for — not attributes of it, and re-pointing a manifest at
/// a different vehicle is not an edit: the Goods Movement Reference named a crossing. A vehicle
/// that leaves the convoy is withdrawn from the truck list, which keeps this manifest intact.
/// </remarks>
public sealed record UpdateManifestCommand(string Id, string? DeliveryNotes, bool FerryBookingComplete);

public enum UpdateManifestOutcome
{
    Updated,
    NotFound,
    Frozen
}

public sealed class UpdateManifestHandler(IManifestRepository repository)
    : ICommandHandler<UpdateManifestCommand, UpdateManifestOutcome>
{
    public async Task<UpdateManifestOutcome> HandleAsync(
        UpdateManifestCommand command, CancellationToken cancellationToken)
    {
        var manifest = await repository.GetByIdAsync(command.Id, cancellationToken);

        if (manifest is null)
        {
            return UpdateManifestOutcome.NotFound;
        }

        // §5.2: once the GMR exists, HMRC has been told what is crossing the border. Changing
        // the manifest afterwards would mean the vehicle arrives carrying something else.
        if (manifest.Frozen)
        {
            return UpdateManifestOutcome.Frozen;
        }

        var updated = await repository.UpdateAsync(
            manifest with
            {
                DeliveryNotes = command.DeliveryNotes,
                FerryBookingComplete = command.FerryBookingComplete,
            },
            cancellationToken);

        return updated ? UpdateManifestOutcome.Updated : UpdateManifestOutcome.NotFound;
    }
}

/// <summary>Remove a manifest that was never used.</summary>
public sealed record DeleteManifestCommand(string Id);

public enum DeleteManifestOutcome
{
    Deleted,
    NotFound,
    Frozen
}

public sealed class DeleteManifestHandler(IManifestRepository repository)
    : ICommandHandler<DeleteManifestCommand, DeleteManifestOutcome>
{
    public async Task<DeleteManifestOutcome> HandleAsync(
        DeleteManifestCommand command, CancellationToken cancellationToken)
    {
        var manifest = await repository.GetByIdAsync(command.Id, cancellationToken);

        if (manifest is null)
        {
            return DeleteManifestOutcome.NotFound;
        }

        // Deleting is the most complete edit there is. A manifest HMRC has been told about has
        // to remain answerable for, whatever happened to the load.
        if (manifest.Frozen)
        {
            return DeleteManifestOutcome.Frozen;
        }

        var deleted = await repository.DeleteAsync(command.Id, cancellationToken);

        return deleted ? DeleteManifestOutcome.Deleted : DeleteManifestOutcome.NotFound;
    }
}

/// <summary>Fetch one manifest, or <c>null</c> if there is no such manifest.</summary>
public sealed record GetManifestByIdQuery(string Id);

public sealed class GetManifestByIdHandler(IManifestRepository repository)
    : IQueryHandler<GetManifestByIdQuery, ManifestReadModel?>
{
    public Task<ManifestReadModel?> HandleAsync(GetManifestByIdQuery query, CancellationToken cancellationToken)
        => repository.GetByIdAsync(query.Id, cancellationToken);
}

/// <summary>A page of manifests. Page size is clamped to 1..200.</summary>
public sealed record ListManifestsQuery(int Page, int PageSize);

public sealed class ListManifestsHandler(IManifestRepository repository)
    : IQueryHandler<ListManifestsQuery, IReadOnlyList<ManifestReadModel>>
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    public Task<IReadOnlyList<ManifestReadModel>> HandleAsync(
        ListManifestsQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > MaxPageSize ? DefaultPageSize : query.PageSize;

        return repository.ListAsync(page, pageSize, cancellationToken);
    }
}
