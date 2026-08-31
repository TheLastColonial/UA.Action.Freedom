using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Manifests;

/// <summary>
/// Open a manifest. The reference is supplied by the caller — it is a document number people
/// say out loud at a border, so it is a natural key like a VIN rather than a minted identifier.
/// </summary>
public sealed record CreateManifestCommand(
    string Id, string? Vin, int? ConvoyId, string? DeliveryNotes, bool FerryBookingComplete);

public enum CreateManifestOutcome
{
    Created,
    Conflict
}

public sealed class CreateManifestHandler(IManifestRepository repository)
    : ICommandHandler<CreateManifestCommand, CreateManifestOutcome>
{
    public async Task<CreateManifestOutcome> HandleAsync(
        CreateManifestCommand command, CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(command.Id, cancellationToken))
        {
            return CreateManifestOutcome.Conflict;
        }

        // A manifest is Created before it is populated — no vehicle, no teams, no cargo required
        // yet. That is the first state of the diagram, and proposing it is what asserts it is
        // complete enough to look at.
        await repository.AddAsync(
            new ManifestReadModel(
                command.Id,
                command.Vin,
                command.ConvoyId,
                ManifestStatus.Created,
                command.DeliveryNotes,
                command.FerryBookingComplete,
                GmrSubmittedAt: null),
            cancellationToken);

        return CreateManifestOutcome.Created;
    }
}

/// <summary>Change the vehicle, convoy, notes or ferry booking on a manifest.</summary>
public sealed record UpdateManifestCommand(
    string Id, string? Vin, int? ConvoyId, string? DeliveryNotes, bool FerryBookingComplete);

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
                Vin = command.Vin,
                ConvoyId = command.ConvoyId,
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
