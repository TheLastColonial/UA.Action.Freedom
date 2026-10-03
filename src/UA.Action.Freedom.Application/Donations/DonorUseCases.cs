using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Donations;

/// <summary>Record a donor. Two donors may share a name, so there is no natural key.</summary>
public sealed record CreateDonorCommand(string Name, string? Email, string? Phone);

/// <summary>
/// Creating a donor cannot conflict, so this returns the identifier it minted. A <see cref="Guid"/>, generated
/// here, so a URL does not count the charity's donors.
/// </summary>
public sealed class CreateDonorHandler(IDonorRepository repository) : ICommandHandler<CreateDonorCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateDonorCommand command, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();

        await repository.AddAsync(new DonorReadModel(id, command.Name, command.Email, command.Phone), cancellationToken);

        return id;
    }
}

public sealed record UpdateDonorCommand(Guid Id, string Name, string? Email, string? Phone);

public enum UpdateDonorOutcome
{
    Updated,
    NotFound
}

public sealed class UpdateDonorHandler(IDonorRepository repository) : ICommandHandler<UpdateDonorCommand, UpdateDonorOutcome>
{
    public async Task<UpdateDonorOutcome> HandleAsync(UpdateDonorCommand command, CancellationToken cancellationToken) =>
        await repository.UpdateAsync(
            new DonorReadModel(command.Id, command.Name, command.Email, command.Phone), cancellationToken)
            ? UpdateDonorOutcome.Updated
            : UpdateDonorOutcome.NotFound;
}

/// <summary>Erase the donor with this identifier: their personal data is deleted and their donations stay.</summary>
public sealed record EraseDonorCommand(Guid Id);

public enum EraseDonorOutcome
{
    Erased,
    NotFound
}

/// <summary>Never refused for being in use: a donor has no operational dependency (ADR 0013).</summary>
public sealed class EraseDonorHandler(IDonorRepository repository) : ICommandHandler<EraseDonorCommand, EraseDonorOutcome>
{
    public async Task<EraseDonorOutcome> HandleAsync(EraseDonorCommand command, CancellationToken cancellationToken) =>
        await repository.EraseAsync(command.Id, cancellationToken) ? EraseDonorOutcome.Erased : EraseDonorOutcome.NotFound;
}

/// <summary>Fetch one donor, or <c>null</c> if there is no such donor (an erased donor is not found).</summary>
public sealed record GetDonorByIdQuery(Guid Id);

public sealed class GetDonorByIdHandler(IDonorRepository repository) : IQueryHandler<GetDonorByIdQuery, DonorReadModel?>
{
    public Task<DonorReadModel?> HandleAsync(GetDonorByIdQuery query, CancellationToken cancellationToken) =>
        repository.GetByIdAsync(query.Id, cancellationToken);
}

/// <summary>A page of donors ordered by name. Page is 1-based; page size is clamped to 1..200.</summary>
public sealed record ListDonorsQuery(int Page, int PageSize);

public sealed class ListDonorsHandler(IDonorRepository repository)
    : IQueryHandler<ListDonorsQuery, IReadOnlyList<DonorReadModel>>
{
    public Task<IReadOnlyList<DonorReadModel>> HandleAsync(ListDonorsQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Clamp(query.Page, query.PageSize);
        return repository.ListAsync(page, pageSize, cancellationToken);
    }
}

internal static class Paging
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    public static (int Page, int PageSize) Clamp(int page, int pageSize) =>
        (page < 1 ? 1 : page, pageSize is < 1 or > MaxPageSize ? DefaultPageSize : pageSize);
}
