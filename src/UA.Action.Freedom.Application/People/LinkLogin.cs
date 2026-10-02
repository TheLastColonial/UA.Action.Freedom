using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.People;

/// <summary>Link the login with this token subject to the volunteer.</summary>
public sealed record LinkLoginCommand(Guid PersonId, string Subject);

public enum LinkLoginOutcome
{
    Linked,
    NotFound,
    SubjectInUse
}

public sealed class LinkLoginHandler(IPersonRepository repository)
    : ICommandHandler<LinkLoginCommand, LinkLoginOutcome>
{
    public async Task<LinkLoginOutcome> HandleAsync(LinkLoginCommand command, CancellationToken cancellationToken) =>
        await repository.LinkLoginAsync(command.PersonId, command.Subject, cancellationToken) switch
        {
            LinkLoginResult.Linked => LinkLoginOutcome.Linked,
            LinkLoginResult.SubjectInUse => LinkLoginOutcome.SubjectInUse,
            _ => LinkLoginOutcome.NotFound,
        };
}
