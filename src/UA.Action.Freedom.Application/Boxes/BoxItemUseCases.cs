using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Categories;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Boxes;

/// <summary>The contents of a box, or <c>null</c> if there is no such box.</summary>
public sealed record ListBoxItemsQuery(int BoxId);

/// <summary>
/// Lists a box's items, each read with what its category says about it: the name, whether the convoy will not
/// carry it, and whether it has expired or is short-dated. Worked out here, from the category, so the persisted
/// row stays only what somebody entered and the answer cannot go stale.
/// </summary>
public sealed class ListBoxItemsHandler(IBoxRepository repository, IItemCategoryRepository categories)
    : IQueryHandler<ListBoxItemsQuery, IReadOnlyList<BoxItemReadModel>?>
{
    public async Task<IReadOnlyList<BoxItemReadModel>?> HandleAsync(
        ListBoxItemsQuery query, CancellationToken cancellationToken)
    {
        // An empty box and a box that does not exist are different answers.
        if (!await repository.ExistsAsync(query.BoxId, cancellationToken))
        {
            return null;
        }

        var items = await repository.ListItemsAsync(query.BoxId, cancellationToken);

        if (items.Count == 0)
        {
            return items;
        }

        var byId = (await categories.ListAsync(cancellationToken)).ToDictionary(category => category.Id);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return items.Select(item => ItemReading.Of(item, byId.GetValueOrDefault(item.CategoryId), today)).ToList();
    }
}

/// <summary>What a category adds to how an item reads.</summary>
internal static class ItemReading
{
    public static BoxItemReadModel Of(BoxItemReadModel item, ItemCategoryReadModel? category, DateOnly today) =>
        item with
        {
            CategoryNameEn = category?.NameEn,
            IsNotCarried = category?.IsNotCarried ?? false,
            ShelfLife = ShelfLife.Assess(item.ExpiresOn, category?.ShelfLife ?? ShelfLifeRule.None, today),
        };
}

/// <summary>Pack a donated item into a box.</summary>
/// <remarks>
/// <paramref name="CommodityCode"/> is optional here but not at a border: ICS2 requires at least six
/// digits per goods item, and <c>GET /manifests/{id}/ens/filing-sheet</c> reports any item with neither a
/// code of its own nor one on its category as missing. Optional because a packer working through a donation
/// should not be blocked on classification — the gap is reported later, by name, while there is still time
/// to close it.
/// </remarks>
public sealed record AddBoxItemCommand(
    int BoxId,
    string Description,
    IReadOnlyDictionary<string, string> Properties,
    int CategoryId,
    string? CommodityCode = null,
    int? Quantity = null,
    decimal? ValueGbp = null,
    ValueSource? ValueSource = null,
    DateOnly? ExpiresOn = null);

public enum AddBoxItemOutcome
{
    Added,
    BoxNotFound,
    AlreadyValidated,
    CategoryNotFound
}

/// <summary>Something worth telling the packer about an item that was nonetheless accepted.</summary>
public enum ItemWarning
{
    /// <summary>The convoy does not carry this kind of goods (D21).</summary>
    NotCarried,

    /// <summary>It expires within its category's short-dated window (D25).</summary>
    ShortShelfLife,

    /// <summary>It has already expired. The box cannot be validated while it is in it (D1).</summary>
    Expired,
}

/// <summary>The outcome, and when the item was packed its identifier and any warnings about it.</summary>
public sealed record AddBoxItemResult(
    AddBoxItemOutcome Outcome, Guid ItemId = default, IReadOnlyList<ItemWarning>? Warnings = null);

/// <summary>
/// Adding to a validated box is refused, which is the rule that gives validation its meaning.
/// </summary>
/// <remarks>
/// The Loader's check covers the contents and the weight. If an item could be packed afterwards
/// the box would travel with a confirmed weight that no longer matches what is inside it, and
/// the border check would be relying on a number nobody had verified for that load.
///
/// An already-expired or not-carried item is accepted and warned about, not refused: the Loader needs to see
/// it to deal with it, and validation is where an expired one is stopped.
/// </remarks>
public sealed class AddBoxItemHandler(IBoxRepository repository, IItemCategoryRepository categories)
    : ICommandHandler<AddBoxItemCommand, AddBoxItemResult>
{
    public async Task<AddBoxItemResult> HandleAsync(AddBoxItemCommand command, CancellationToken cancellationToken)
    {
        var box = await repository.GetByIdAsync(command.BoxId, cancellationToken);

        if (box is null)
        {
            return new AddBoxItemResult(AddBoxItemOutcome.BoxNotFound);
        }

        if (box.Validated)
        {
            return new AddBoxItemResult(AddBoxItemOutcome.AlreadyValidated);
        }

        var category = await categories.GetByIdAsync(command.CategoryId, cancellationToken);

        if (category is null)
        {
            return new AddBoxItemResult(AddBoxItemOutcome.CategoryNotFound);
        }

        var item = new BoxItemReadModel(
            Guid.NewGuid(), command.Description, command.Properties, command.CategoryId,
            command.CommodityCode, command.Quantity, command.ValueGbp, command.ValueSource, command.ExpiresOn);

        await repository.AddItemAsync(command.BoxId, item, cancellationToken);

        return new AddBoxItemResult(
            AddBoxItemOutcome.Added, item.Id, WarningsFor(item, category));
    }

    private static List<ItemWarning> WarningsFor(BoxItemReadModel item, ItemCategoryReadModel category)
    {
        var warnings = new List<ItemWarning>();

        if (category.IsNotCarried)
        {
            warnings.Add(ItemWarning.NotCarried);
        }

        switch (ShelfLife.Assess(item.ExpiresOn, category.ShelfLife, DateOnly.FromDateTime(DateTime.UtcNow)))
        {
            case ShelfLifeStatus.Expired:
                warnings.Add(ItemWarning.Expired);
                break;
            case ShelfLifeStatus.Short:
                warnings.Add(ItemWarning.ShortShelfLife);
                break;
        }

        return warnings;
    }
}

/// <summary>Take an item back out of a box.</summary>
public sealed record RemoveBoxItemCommand(int BoxId, Guid ItemId);

public enum RemoveBoxItemOutcome
{
    Removed,
    NotFound,
    AlreadyValidated
}

public sealed class RemoveBoxItemHandler(IBoxRepository repository)
    : ICommandHandler<RemoveBoxItemCommand, RemoveBoxItemOutcome>
{
    public async Task<RemoveBoxItemOutcome> HandleAsync(
        RemoveBoxItemCommand command, CancellationToken cancellationToken)
    {
        var box = await repository.GetByIdAsync(command.BoxId, cancellationToken);

        if (box is null)
        {
            return RemoveBoxItemOutcome.NotFound;
        }

        // Same rule in the other direction: removing an item would leave the confirmed weight
        // describing more than the box now holds.
        if (box.Validated)
        {
            return RemoveBoxItemOutcome.AlreadyValidated;
        }

        return await repository.DeleteItemAsync(command.BoxId, command.ItemId, cancellationToken)
            ? RemoveBoxItemOutcome.Removed
            : RemoveBoxItemOutcome.NotFound;
    }
}
