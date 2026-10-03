using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Categories;

/// <summary>Add a category of donated item. It is never fixed: only the seeded list is.</summary>
public sealed record CreateCategoryCommand(
    string NameEn, string NameUk, int? HazardClass, bool IsSensitive, bool IsNotCarried, int? WarnWithinDays);

public enum CreateCategoryOutcome
{
    Created,
    NameTaken
}

public sealed record CreateCategoryResult(CreateCategoryOutcome Outcome, int Id = 0);

public sealed class CreateCategoryHandler(IItemCategoryRepository repository)
    : ICommandHandler<CreateCategoryCommand, CreateCategoryResult>
{
    public async Task<CreateCategoryResult> HandleAsync(
        CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        var id = await repository.AddAsync(
            new ItemCategoryReadModel(
                Id: 0, command.NameEn, command.NameUk, IsFixed: false, command.HazardClass,
                command.IsSensitive, command.IsNotCarried, command.WarnWithinDays),
            cancellationToken);

        return id is { } created
            ? new CreateCategoryResult(CreateCategoryOutcome.Created, created)
            : new CreateCategoryResult(CreateCategoryOutcome.NameTaken);
    }
}

/// <summary>Change a category's names, flags or shelf-life rule. Codes have their own command.</summary>
public sealed record UpdateCategoryCommand(
    int Id, string NameEn, string NameUk, int? HazardClass, bool IsSensitive, bool IsNotCarried, int? WarnWithinDays);

public enum UpdateCategoryOutcome
{
    Updated,
    NotFound,
    NameTaken
}

public sealed class UpdateCategoryHandler(IItemCategoryRepository repository)
    : ICommandHandler<UpdateCategoryCommand, UpdateCategoryOutcome>
{
    public async Task<UpdateCategoryOutcome> HandleAsync(
        UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        var updated = await repository.UpdateAsync(
            new ItemCategoryReadModel(
                command.Id, command.NameEn, command.NameUk, IsFixed: false, command.HazardClass,
                command.IsSensitive, command.IsNotCarried, command.WarnWithinDays),
            cancellationToken);

        return updated switch
        {
            true => UpdateCategoryOutcome.Updated,
            false => UpdateCategoryOutcome.NotFound,
            null => UpdateCategoryOutcome.NameTaken,
        };
    }
}

/// <summary>Map a category to the code an authority wants, or clear the mapping with a <see langword="null"/> code (O31).</summary>
public sealed record SetCategoryCodeCommand(int Id, CustomsAuthority Authority, string? Code);

public enum SetCategoryCodeOutcome
{
    Set,
    NotFound
}

public sealed class SetCategoryCodeHandler(IItemCategoryRepository repository)
    : ICommandHandler<SetCategoryCodeCommand, SetCategoryCodeOutcome>
{
    public async Task<SetCategoryCodeOutcome> HandleAsync(
        SetCategoryCodeCommand command, CancellationToken cancellationToken) =>
        await repository.SetCodeAsync(command.Id, command.Authority, command.Code, cancellationToken)
            ? SetCategoryCodeOutcome.Set
            : SetCategoryCodeOutcome.NotFound;
}

/// <summary>Fetch one category, or <see langword="null"/> if there is no such category.</summary>
public sealed record GetCategoryByIdQuery(int Id);

public sealed class GetCategoryByIdHandler(IItemCategoryRepository repository)
    : IQueryHandler<GetCategoryByIdQuery, ItemCategoryReadModel?>
{
    public Task<ItemCategoryReadModel?> HandleAsync(GetCategoryByIdQuery query, CancellationToken cancellationToken)
        => repository.GetByIdAsync(query.Id, cancellationToken);
}

/// <summary>Every category.</summary>
public sealed record ListCategoriesQuery;

public sealed class ListCategoriesHandler(IItemCategoryRepository repository)
    : IQueryHandler<ListCategoriesQuery, IReadOnlyList<ItemCategoryReadModel>>
{
    public Task<IReadOnlyList<ItemCategoryReadModel>> HandleAsync(
        ListCategoriesQuery query, CancellationToken cancellationToken)
        => repository.ListAsync(cancellationToken);
}
