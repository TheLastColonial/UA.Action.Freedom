using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Unit.People;

/// <summary>
/// Removing a volunteer. Deletion is how a volunteer who has left stops being reachable, so
/// "there was no such person" and "removed them" have to be distinguishable to the caller.
/// </summary>
public class DeletePersonHandlerTests
{
    [Fact]
    public async Task Reports_deleted_when_a_row_was_removed()
    {
        var repository = Substitute.For<IPersonRepository>();
        repository.DeleteAsync(PersonTestData.Id, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new DeletePersonHandler(repository);

        var outcome = await handler.HandleAsync(new DeletePersonCommand(PersonTestData.Id), CancellationToken.None);

        outcome.Should().Be(DeletePersonOutcome.Deleted);
    }

    [Fact]
    public async Task Reports_not_found_when_there_was_no_such_person()
    {
        var repository = Substitute.For<IPersonRepository>();
        repository.DeleteAsync(PersonTestData.Id, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new DeletePersonHandler(repository);

        var outcome = await handler.HandleAsync(new DeletePersonCommand(PersonTestData.Id), CancellationToken.None);

        outcome.Should().Be(DeletePersonOutcome.NotFound);
    }
}
