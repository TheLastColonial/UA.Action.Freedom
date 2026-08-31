using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Boxes;

/// <summary>The contents of a box, or <c>null</c> if there is no such box.</summary>
public sealed record ListBoxItemsQuery(int BoxId);

public sealed class ListBoxItemsHandler(IBoxRepository repository)
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

        return await repository.ListItemsAsync(query.BoxId, cancellationToken);
    }
}

/// <summary>Pack a donated item into a box.</summary>
public sealed record AddBoxItemCommand(
    int BoxId, string Description, IReadOnlyDictionary<string, string> Properties);

public enum AddBoxItemOutcome
{
    Added,
    BoxNotFound,
    AlreadyValidated
}

/// <summary>
/// Adding to a validated box is refused, which is the rule that gives validation its meaning.
/// </summary>
/// <remarks>
/// The Loader's check covers the contents and the weight. If an item could be packed afterwards
/// the box would travel with a confirmed weight that no longer matches what is inside it, and
/// the border check would be relying on a number nobody had verified for that load.
/// </remarks>
public sealed class AddBoxItemHandler(IBoxRepository repository)
    : ICommandHandler<AddBoxItemCommand, AddBoxItemOutcome>
{
    public async Task<AddBoxItemOutcome> HandleAsync(AddBoxItemCommand command, CancellationToken cancellationToken)
    {
        var box = await repository.GetByIdAsync(command.BoxId, cancellationToken);

        if (box is null)
        {
            return AddBoxItemOutcome.BoxNotFound;
        }

        if (box.Validated)
        {
            return AddBoxItemOutcome.AlreadyValidated;
        }

        await repository.AddItemAsync(
            command.BoxId,
            new BoxItemReadModel(Guid.NewGuid(), command.Description, command.Properties),
            cancellationToken);

        return AddBoxItemOutcome.Added;
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
