using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.People;

/// <summary>Remove the volunteer with this identifier.</summary>
public sealed record DeletePersonCommand(Guid Id);

public enum DeletePersonOutcome
{
    Deleted,
    NotFound,
    StillReferenced
}

public sealed class DeletePersonHandler(IPersonRepository repository)
    : ICommandHandler<DeletePersonCommand, DeletePersonOutcome>
{
    public async Task<DeletePersonOutcome> HandleAsync(DeletePersonCommand command, CancellationToken cancellationToken) =>
        await repository.DeleteAsync(command.Id, cancellationToken) switch
        {
            DeletePersonResult.Deleted => DeletePersonOutcome.Deleted,
            DeletePersonResult.StillReferenced => DeletePersonOutcome.StillReferenced,
            _ => DeletePersonOutcome.NotFound,
        };
}
