using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Categories;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Dictionary-backed category persistence so the endpoint tests run without a database. Enforces what the SQL does:
/// a name is unique, <c>IsFixed</c> cannot be written, and a code is cleared by <see langword="null"/>.
/// </summary>
internal sealed class InMemoryItemCategoryRepository : IItemCategoryRepository, IRecordsWhoChanged
{
    private readonly Dictionary<int, ItemCategoryReadModel> categories = [];

    private readonly ChangeLedger<int> changes = new();

    private int nextId = 1;

    public InMemoryItemCategoryRepository(params ItemCategoryReadModel[] seed)
    {
        foreach (var category in seed)
        {
            categories[category.Id] = category;
            nextId = Math.Max(nextId, category.Id + 1);
        }
    }

    public void Attach(IChangeAttribution attribution, IPersonRepository people) =>
        changes.Attach(attribution, people);

    public ItemCategoryReadModel? Category(int id) => categories.GetValueOrDefault(id);

    private ItemCategoryReadModel Read(ItemCategoryReadModel category)
    {
        var (name, at) = changes.Of(category.Id);
        return category with { LastChangedByName = name, LastChangedAt = at };
    }

    private bool NameTaken(string name, int exceptId) =>
        categories.Values.Any(other =>
            other.Id != exceptId && string.Equals(other.NameEn, name, StringComparison.OrdinalIgnoreCase));

    public Task<ItemCategoryReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(categories.TryGetValue(id, out var category) ? Read(category) : null);

    public Task<IReadOnlyList<ItemCategoryReadModel>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ItemCategoryReadModel>>(
            categories.Values.OrderByDescending(category => category.IsFixed).ThenBy(category => category.NameEn)
                .Select(Read).ToList());

    public Task<int?> AddAsync(ItemCategoryReadModel category, CancellationToken cancellationToken)
    {
        if (NameTaken(category.NameEn, exceptId: 0))
        {
            return Task.FromResult<int?>(null);
        }

        var id = nextId++;
        categories[id] = category with { Id = id, IsFixed = false, UkCode = null, EuCode = null, UaCode = null };
        changes.Stamp(id);
        return Task.FromResult<int?>(id);
    }

    public Task<bool?> UpdateAsync(ItemCategoryReadModel category, CancellationToken cancellationToken)
    {
        if (!categories.TryGetValue(category.Id, out var existing))
        {
            return Task.FromResult<bool?>(false);
        }

        if (NameTaken(category.NameEn, category.Id))
        {
            return Task.FromResult<bool?>(null);
        }

        categories[category.Id] = category with
        {
            IsFixed = existing.IsFixed, UkCode = existing.UkCode, EuCode = existing.EuCode, UaCode = existing.UaCode,
        };
        changes.Stamp(category.Id);
        return Task.FromResult<bool?>(true);
    }

    public Task<bool> SetCodeAsync(int id, CustomsAuthority authority, string? code, CancellationToken cancellationToken)
    {
        if (!categories.TryGetValue(id, out var existing))
        {
            return Task.FromResult(false);
        }

        categories[id] = authority switch
        {
            CustomsAuthority.UK => existing with { UkCode = code },
            CustomsAuthority.EU => existing with { EuCode = code },
            _ => existing with { UaCode = code },
        };
        changes.Stamp(id);
        return Task.FromResult(true);
    }
}
