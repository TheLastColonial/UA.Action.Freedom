using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.People;

/// <summary>
/// A page of volunteers ordered by name. Page is 1-based; page size is clamped to 1..200.
/// <see cref="DriversOnly"/> narrows the page to volunteers who drive.
/// </summary>
public sealed record ListPeopleQuery(int Page, int PageSize, bool DriversOnly);

public sealed class ListPeopleHandler(IPersonRepository repository)
    : IQueryHandler<ListPeopleQuery, IReadOnlyList<PersonReadModel>>
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    public Task<IReadOnlyList<PersonReadModel>> HandleAsync(ListPeopleQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > MaxPageSize ? DefaultPageSize : query.PageSize;

        return repository.ListAsync(page, pageSize, query.DriversOnly, cancellationToken);
    }
}
