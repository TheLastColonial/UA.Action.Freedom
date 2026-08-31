using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.People;

/// <summary>Replace every mutable field of the volunteer identified by <see cref="Id"/>.</summary>
public sealed record UpdatePersonCommand(
    Guid Id,
    string FirstName,
    string LastName,
    DateTime DateOfBirth,
    DateTime Joined,
    string? Phone,
    bool IsDriver,
    bool Committed);

public enum UpdatePersonOutcome
{
    Updated,
    NotFound
}

public sealed class UpdatePersonHandler(IPersonRepository repository)
    : ICommandHandler<UpdatePersonCommand, UpdatePersonOutcome>
{
    public async Task<UpdatePersonOutcome> HandleAsync(UpdatePersonCommand command, CancellationToken cancellationToken)
    {
        var updated = await repository.UpdateAsync(
            new PersonReadModel(
                command.Id,
                command.FirstName,
                command.LastName,
                command.DateOfBirth,
                command.Joined,
                command.Phone,
                command.IsDriver,
                command.Committed),
            cancellationToken);

        return updated ? UpdatePersonOutcome.Updated : UpdatePersonOutcome.NotFound;
    }
}
