using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Unit.People;

/// <summary>
/// Removing a volunteer. Deletion is how a volunteer who has left stops being reachable, so
/// "there was no such person" and "removed them" have to be distinguishable to the caller — and
/// so does "they are still named on a crew, a manifest or a box record", which must not vanish.
/// </summary>
public class DeletePersonHandlerTests
{
    [Theory]
    [InlineData(DeletePersonResult.Deleted, DeletePersonOutcome.Deleted)]
    [InlineData(DeletePersonResult.NotFound, DeletePersonOutcome.NotFound)]
    [InlineData(DeletePersonResult.StillReferenced, DeletePersonOutcome.StillReferenced)]
    public async Task Reports_what_the_delete_found(DeletePersonResult result, DeletePersonOutcome expected)
    {
        var repository = Substitute.For<IPersonRepository>();
        repository.DeleteAsync(PersonTestData.Id, Arg.Any<CancellationToken>()).Returns(result);
        var handler = new DeletePersonHandler(repository);

        var outcome = await handler.HandleAsync(new DeletePersonCommand(PersonTestData.Id), TestContext.Current.CancellationToken);

        outcome.Should().Be(expected);
    }
}
