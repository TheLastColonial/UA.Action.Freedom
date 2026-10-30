using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Boxes;

/// <summary>Start a box: where it is, and who it is ultimately for.</summary>
public sealed record CreateBoxCommand(Guid? ReceiverRef, int? LocationId);

public enum CreateBoxOutcome
{
    Created,
    ReceiverNotFound,
    ReceiverNotRegistered
}

/// <summary>The outcome, and the new box identifier when it was created.</summary>
public sealed record CreateBoxResult(CreateBoxOutcome Outcome, int BoxId = 0);

public sealed class CreateBoxHandler(IBoxRepository repository, IReceiverRepository receivers)
    : ICommandHandler<CreateBoxCommand, CreateBoxResult>
{
    public async Task<CreateBoxResult> HandleAsync(CreateBoxCommand command, CancellationToken cancellationToken)
    {
        // A destination has to be a receiver that is registered: Ukraine goods list is filed by the
        // Receiver, and one that is not registered cannot file it (ADR 0012). Refusing here, rather than
        // leaning on the foreign key, turns a bad reference into an answer instead of a SqlException.
        if (command.ReceiverRef is { } receiverRef)
        {
            switch (await ReceiverEligibilityCheck.CheckAsync(receivers, receiverRef, cancellationToken))
            {
                case ReceiverEligibility.NotFound:
                    return new CreateBoxResult(CreateBoxOutcome.ReceiverNotFound);
                case ReceiverEligibility.NotRegistered:
                    return new CreateBoxResult(CreateBoxOutcome.ReceiverNotRegistered);
            }
        }

        // Weight starts at zero and stays there until a Loader validates the box. An unvalidated
        // weight on a border document would be a guess presented as a fact.
        var id = await repository.AddAsync(
            new BoxReadModel(
                Id: 0,
                WeightKg: 0,
                WidthCm: null,
                DepthCm: null,
                HeightCm: null,
                command.ReceiverRef,
                command.LocationId,
                ValidatedByPersonId: null,
                ValidatedAt: null),
            cancellationToken);

        return new CreateBoxResult(CreateBoxOutcome.Created, id);
    }
}

/// <summary>Move a box to a different location, or point it at a different receiver.</summary>
public sealed record UpdateBoxCommand(int Id, Guid? ReceiverRef, int? LocationId);

public enum UpdateBoxOutcome
{
    Updated,
    NotFound,
    AlreadyValidated,
    ReceiverNotFound,
    ReceiverNotRegistered
}

public sealed class UpdateBoxHandler(IBoxRepository repository, IReceiverRepository receivers)
    : ICommandHandler<UpdateBoxCommand, UpdateBoxOutcome>
{
    public async Task<UpdateBoxOutcome> HandleAsync(UpdateBoxCommand command, CancellationToken cancellationToken)
    {
        var box = await repository.GetByIdAsync(command.Id, cancellationToken);

        if (box is null)
        {
            return UpdateBoxOutcome.NotFound;
        }

        // A validated box has been checked, weighed and signed for. Re-pointing it at another
        // receiver afterwards would mean the Loader's signature describes a box that no longer
        // exists as they left it.
        if (box.Validated)
        {
            return UpdateBoxOutcome.AlreadyValidated;
        }

        // Only a receiver the box is being newly pointed at has to qualify. Moving a box that already
        // names a receiver which has since been suspended is not a new allocation, and refusing it would
        // make the box impossible to relocate; the suspension shows up as a blocking requirement instead.
        if (command.ReceiverRef is { } receiverRef && command.ReceiverRef != box.ReceiverRef)
        {
            switch (await ReceiverEligibilityCheck.CheckAsync(receivers, receiverRef, cancellationToken))
            {
                case ReceiverEligibility.NotFound:
                    return UpdateBoxOutcome.ReceiverNotFound;
                case ReceiverEligibility.NotRegistered:
                    return UpdateBoxOutcome.ReceiverNotRegistered;
            }
        }

        var updated = await repository.UpdateAsync(
            new BoxReadModel(
                command.Id,
                box.WeightKg,
                box.WidthCm,
                box.DepthCm,
                box.HeightCm,
                command.ReceiverRef,
                command.LocationId,
                box.ValidatedByPersonId,
                box.ValidatedAt),
            cancellationToken);

        return updated ? UpdateBoxOutcome.Updated : UpdateBoxOutcome.NotFound;
    }
}

/// <summary>Remove a box that was never sent.</summary>
public sealed record DeleteBoxCommand(int Id);

public enum DeleteBoxOutcome
{
    Deleted,
    NotFound,
    AlreadyValidated
}

public sealed class DeleteBoxHandler(IBoxRepository repository)
    : ICommandHandler<DeleteBoxCommand, DeleteBoxOutcome>
{
    public async Task<DeleteBoxOutcome> HandleAsync(DeleteBoxCommand command, CancellationToken cancellationToken)
    {
        var box = await repository.GetByIdAsync(command.Id, cancellationToken);

        if (box is null)
        {
            return DeleteBoxOutcome.NotFound;
        }

        // An attested box is a commitment made at border checkpoints, and deleting it would cascade away
        // the item list that was vouched for. If its contents must change the box is replaced, never removed
        // (ADR 0011).
        if (box.Validated)
        {
            return DeleteBoxOutcome.AlreadyValidated;
        }

        var deleted = await repository.DeleteAsync(command.Id, cancellationToken);
        return deleted ? DeleteBoxOutcome.Deleted : DeleteBoxOutcome.NotFound;
    }
}

/// <summary>Replace an attested box: void it and create a new unattested box with the same items.</summary>
public sealed record ReplaceBoxCommand(int Id);

public enum ReplaceBoxOutcome
{
    Replaced,
    NotFound,
    NotAttested,
    AlreadyVoided,
    LoadFrozen
}

/// <summary>The outcome, and the new box identifier when it was replaced.</summary>
public sealed record ReplaceBoxResult(ReplaceBoxOutcome Outcome, int ReplacementBoxId = 0);

/// <summary>
/// An attested box is never edited, and never unlocked: its attestation is who vouched for what, and rewriting
/// it would make that a claim anybody could change. If its contents must change it is voided and replaced, and
/// the replacement is attested afresh by a Loader. A box nobody has attested is simply edited, so replacing one
/// is refused.
/// </summary>
/// <remarks>
/// Transitional: replacing moves the cargo allocation to the new box, which is a change to the vehicle's load, so
/// it is refused while the manifest's GMR freeze stands (plan 15 replaces the freeze with a return to Proposed).
/// </remarks>
public sealed class ReplaceBoxHandler(
    IBoxRepository repository,
    IConvoyVehicleRepository truckList,
    IManifestRepository manifests)
    : ICommandHandler<ReplaceBoxCommand, ReplaceBoxResult>
{
    public async Task<ReplaceBoxResult> HandleAsync(ReplaceBoxCommand command, CancellationToken cancellationToken)
    {
        var box = await repository.GetByIdAsync(command.Id, cancellationToken);

        if (Refusal(box) is { } refusal)
        {
            return new ReplaceBoxResult(refusal);
        }

        var allocation = await truckList.GetBoxAllocationAsync(command.Id, cancellationToken);

        if (allocation is not null
            && await manifests.IsLoadFrozenAsync(allocation.ConvoyId.Value, allocation.Vin, cancellationToken))
        {
            return new ReplaceBoxResult(ReplaceBoxOutcome.LoadFrozen);
        }

        if (await repository.ReplaceAsync(command.Id, cancellationToken) is { } replacementId)
        {
            return new ReplaceBoxResult(ReplaceBoxOutcome.Replaced, replacementId);
        }

        // Lost a race: another Loader replaced it first. Say why from what the box is now.
        return new ReplaceBoxResult(
            Refusal(await repository.GetByIdAsync(command.Id, cancellationToken)) ?? ReplaceBoxOutcome.NotFound);
    }

    private static ReplaceBoxOutcome? Refusal(BoxReadModel? box) => box switch
    {
        null => ReplaceBoxOutcome.NotFound,
        { Voided: true } => ReplaceBoxOutcome.AlreadyVoided,
        { Validated: false } => ReplaceBoxOutcome.NotAttested,
        _ => null,
    };
}

/// <summary>Fetch one box, or <c>null</c> if there is no such box.</summary>
public sealed record GetBoxByIdQuery(int Id);

public sealed class GetBoxByIdHandler(IBoxRepository repository)
    : IQueryHandler<GetBoxByIdQuery, BoxReadModel?>
{
    public Task<BoxReadModel?> HandleAsync(GetBoxByIdQuery query, CancellationToken cancellationToken)
        => repository.GetByIdAsync(query.Id, cancellationToken);
}

/// <summary>
/// A page of boxes. Page size is clamped to 1..200. <paramref name="Visibility"/> has no default, so a caller has to
/// decide which locations it may see (ADR 0010).
/// </summary>
public sealed record ListBoxesQuery(int Page, int PageSize, LocationVisibility Visibility);

public sealed class ListBoxesHandler(IBoxRepository repository)
    : IQueryHandler<ListBoxesQuery, IReadOnlyList<BoxReadModel>>
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    public Task<IReadOnlyList<BoxReadModel>> HandleAsync(ListBoxesQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > MaxPageSize ? DefaultPageSize : query.PageSize;

        return repository.ListAsync(page, pageSize, query.Visibility, cancellationToken);
    }
}

/// <summary>
/// A Loader confirms what is in the box and what it weighs.
/// </summary>
/// <remarks>
/// The trust boundary between the donor and Ukrainian Action. Contents are verified so they can
/// be weighed for border checks and so the charity can vouch for what it is carrying, and the
/// record of who did it and when is the artefact that makes that vouching mean something
/// (docs/domain/key-concepts.md § Box). It happens once.
/// </remarks>
public sealed record ValidateBoxCommand(
    int Id, Guid ValidatedByPersonId, int WeightKg,
    decimal? WidthCm = null, decimal? DepthCm = null, decimal? HeightCm = null);

public enum ValidateBoxOutcome
{
    Validated,
    NotFound,
    AlreadyValidated,
    HasExpiredItems
}

public sealed class ValidateBoxHandler(IBoxRepository repository)
    : ICommandHandler<ValidateBoxCommand, ValidateBoxOutcome>
{
    public async Task<ValidateBoxOutcome> HandleAsync(ValidateBoxCommand command, CancellationToken cancellationToken)
    {
        // A Loader cannot vouch for a box with something already past its date in it: it comes out first (D1).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = await repository.ListItemsAsync(command.Id, cancellationToken);

        if (items.Any(item => ShelfLife.Assess(item.ExpiresOn, ShelfLifeRule.None, today) == ShelfLifeStatus.Expired))
        {
            // An already-validated box answers as one: an item that expired since is not a reason to hide that.
            var existing = await repository.GetByIdAsync(command.Id, cancellationToken);
            return existing is { Validated: true } ? ValidateBoxOutcome.AlreadyValidated : ValidateBoxOutcome.HasExpiredItems;
        }

        // The validator is the caller's linked person, resolved at the edge from their login — a
        // volunteer on file by construction, so there is nothing here to look up.
        // Conditional on the box not already being validated, so two Loaders checking the same
        // box at once cannot both record themselves as the one who did it.
        if (await repository.ValidateAsync(
                command.Id, command.ValidatedByPersonId, command.WeightKg,
                command.WidthCm, command.DepthCm, command.HeightCm, DateTime.UtcNow, cancellationToken))
        {
            return ValidateBoxOutcome.Validated;
        }

        var box = await repository.GetByIdAsync(command.Id, cancellationToken);

        return box is null ? ValidateBoxOutcome.NotFound : ValidateBoxOutcome.AlreadyValidated;
    }
}
