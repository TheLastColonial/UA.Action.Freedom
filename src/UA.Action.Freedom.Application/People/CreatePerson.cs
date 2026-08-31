using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.People;

/// <summary>Record a volunteer. Two volunteers may share a name, so there is no natural key.</summary>
public sealed record CreatePersonCommand(
    string FirstName,
    string LastName,
    DateTime DateOfBirth,
    DateTime Joined,
    string? Phone,
    bool IsDriver,
    bool Committed);

/// <summary>
/// Creating a volunteer cannot conflict, so this handler returns the identifier it minted rather
/// than an outcome enum.
/// </summary>
/// <remarks>
/// The identifier is generated here, not by the database. A sequence would put a count of the
/// charity's volunteers into every URL a person appears in; a <see cref="Guid"/> does not, and it
/// is known before the insert, so nothing has to read it back (recommendations §4.8).
/// </remarks>
public sealed class CreatePersonHandler(IPersonRepository repository)
    : ICommandHandler<CreatePersonCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreatePersonCommand command, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();

        await repository.AddAsync(
            new PersonReadModel(
                id,
                command.FirstName,
                command.LastName,
                command.DateOfBirth,
                command.Joined,
                command.Phone,
                command.IsDriver,
                command.Committed),
            cancellationToken);

        return id;
    }
}
