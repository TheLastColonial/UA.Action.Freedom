using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.People;

/// <summary>Fetch one volunteer, or <c>null</c> if there is no such person.</summary>
public sealed record GetPersonByIdQuery(Guid Id);

public sealed class GetPersonByIdHandler(IPersonRepository repository)
    : IQueryHandler<GetPersonByIdQuery, PersonReadModel?>
{
    public Task<PersonReadModel?> HandleAsync(GetPersonByIdQuery query, CancellationToken cancellationToken)
        => repository.GetByIdAsync(query.Id, cancellationToken);
}
