using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.People;

/// <summary>Remove the volunteer with this identifier.</summary>
public sealed record DeletePersonCommand(Guid Id);

public enum DeletePersonOutcome
{
    Deleted,
    NotFound
}

public sealed class DeletePersonHandler(IPersonRepository repository)
    : ICommandHandler<DeletePersonCommand, DeletePersonOutcome>
{
    public async Task<DeletePersonOutcome> HandleAsync(DeletePersonCommand command, CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteAsync(command.Id, cancellationToken);
        return deleted ? DeletePersonOutcome.Deleted : DeletePersonOutcome.NotFound;
    }
}
