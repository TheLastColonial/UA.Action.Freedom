using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Categories;

/// <summary>Persistence port for <see cref="ItemCategoryReadModel"/> and the customs code it maps to per authority.</summary>
public interface IItemCategoryRepository
{
    Task<ItemCategoryReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Every category, fixed ones first then by name. There are few enough that this is not paged.</summary>
    Task<IReadOnlyList<ItemCategoryReadModel>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Inserts a category and returns its identifier, or <see langword="null"/> when the name is taken.</summary>
    Task<int?> AddAsync(ItemCategoryReadModel category, CancellationToken cancellationToken);

    /// <summary>
    /// Updates everything but <c>IsFixed</c>, which no update can change. Returns <see langword="false"/> when there is
    /// no such category, and <see langword="null"/> when the new name is taken by another.
    /// </summary>
    Task<bool?> UpdateAsync(ItemCategoryReadModel category, CancellationToken cancellationToken);

    /// <summary>Sets the code for an authority, or clears it when <paramref name="code"/> is <see langword="null"/>.</summary>
    Task<bool> SetCodeAsync(int id, CustomsAuthority authority, string? code, CancellationToken cancellationToken);
}
