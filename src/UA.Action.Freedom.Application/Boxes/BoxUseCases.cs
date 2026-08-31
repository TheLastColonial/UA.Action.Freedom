using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Application.Boxes;

/// <summary>Start a box: where it is, and who it is ultimately for.</summary>
public sealed record CreateBoxCommand(
    Guid? ReceiverRef, string? House, string? Street, string? City, string? Country, string? Postcode);

public sealed class CreateBoxHandler(IBoxRepository repository)
    : ICommandHandler<CreateBoxCommand, int>
{
    public Task<int> HandleAsync(CreateBoxCommand command, CancellationToken cancellationToken)
        // Weight starts at zero and stays there until a Loader validates the box. An unvalidated
        // weight on a border document would be a guess presented as a fact.
        => repository.AddAsync(
            new BoxReadModel(
                Id: 0,
                WeightKg: 0,
                command.ReceiverRef,
                command.House,
                command.Street,
                command.City,
                command.Country,
                command.Postcode,
                ValidatedByPersonId: null,
                ValidatedAt: null),
            cancellationToken);
}

/// <summary>Move a box, or point it at a different receiver.</summary>
public sealed record UpdateBoxCommand(
    int Id, Guid? ReceiverRef, string? House, string? Street, string? City, string? Country, string? Postcode);

public enum UpdateBoxOutcome
{
    Updated,
    NotFound,
    AlreadyValidated
}

public sealed class UpdateBoxHandler(IBoxRepository repository)
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

        var updated = await repository.UpdateAsync(
            new BoxReadModel(
                command.Id,
                box.WeightKg,
                command.ReceiverRef,
                command.House,
                command.Street,
                command.City,
                command.Country,
                command.Postcode,
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
    NotFound
}

public sealed class DeleteBoxHandler(IBoxRepository repository)
    : ICommandHandler<DeleteBoxCommand, DeleteBoxOutcome>
{
    public async Task<DeleteBoxOutcome> HandleAsync(DeleteBoxCommand command, CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteAsync(command.Id, cancellationToken);
        return deleted ? DeleteBoxOutcome.Deleted : DeleteBoxOutcome.NotFound;
    }
}

/// <summary>Fetch one box, or <c>null</c> if there is no such box.</summary>
public sealed record GetBoxByIdQuery(int Id);

public sealed class GetBoxByIdHandler(IBoxRepository repository)
    : IQueryHandler<GetBoxByIdQuery, BoxReadModel?>
{
    public Task<BoxReadModel?> HandleAsync(GetBoxByIdQuery query, CancellationToken cancellationToken)
        => repository.GetByIdAsync(query.Id, cancellationToken);
}

/// <summary>A page of boxes. Page size is clamped to 1..200.</summary>
public sealed record ListBoxesQuery(int Page, int PageSize);

public sealed class ListBoxesHandler(IBoxRepository repository)
    : IQueryHandler<ListBoxesQuery, IReadOnlyList<BoxReadModel>>
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    public Task<IReadOnlyList<BoxReadModel>> HandleAsync(ListBoxesQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > MaxPageSize ? DefaultPageSize : query.PageSize;

        return repository.ListAsync(page, pageSize, cancellationToken);
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
public sealed record ValidateBoxCommand(int Id, Guid ValidatedByPersonId, int WeightKg);

public enum ValidateBoxOutcome
{
    Validated,
    NotFound,
    AlreadyValidated,
    NoSuchValidator
}

public sealed class ValidateBoxHandler(IBoxRepository repository, IPersonRepository people)
    : ICommandHandler<ValidateBoxCommand, ValidateBoxOutcome>
{
    public async Task<ValidateBoxOutcome> HandleAsync(ValidateBoxCommand command, CancellationToken cancellationToken)
    {
        // The validator has to be a volunteer on file. A signature naming somebody who is not a
        // real person is worse than no signature, because it looks like accountability.
        if (!await people.ExistsAsync(command.ValidatedByPersonId, cancellationToken))
        {
            return ValidateBoxOutcome.NoSuchValidator;
        }

        // Conditional on the box not already being validated, so two Loaders checking the same
        // box at once cannot both record themselves as the one who did it.
        if (await repository.ValidateAsync(
                command.Id, command.ValidatedByPersonId, command.WeightKg, DateTime.UtcNow, cancellationToken))
        {
            return ValidateBoxOutcome.Validated;
        }

        var box = await repository.GetByIdAsync(command.Id, cancellationToken);

        return box is null ? ValidateBoxOutcome.NotFound : ValidateBoxOutcome.AlreadyValidated;
    }
}
