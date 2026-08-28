using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Vehicles;

/// <summary>A page of vehicles ordered by VIN. Page is 1-based; page size is clamped to 1..200.</summary>
public sealed record ListVehiclesQuery(int Page, int PageSize);

public sealed class ListVehiclesHandler(IVehicleRepository repository)
    : IQueryHandler<ListVehiclesQuery, IReadOnlyList<VehicleReadModel>>
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    public Task<IReadOnlyList<VehicleReadModel>> HandleAsync(ListVehiclesQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > MaxPageSize ? DefaultPageSize : query.PageSize;

        return repository.ListAsync(page, pageSize, cancellationToken);
    }
}
