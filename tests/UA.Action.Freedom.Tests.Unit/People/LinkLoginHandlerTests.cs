using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Unit.People;

/// <summary>
/// Linking a login to a volunteer. "Linked", "no such volunteer" and "that login already belongs
/// to someone else" must stay distinguishable: the last is how a mistyped subject is noticed.
/// </summary>
public class LinkLoginHandlerTests
{
    [Theory]
    [InlineData(LinkLoginResult.Linked, LinkLoginOutcome.Linked)]
    [InlineData(LinkLoginResult.NotFound, LinkLoginOutcome.NotFound)]
    [InlineData(LinkLoginResult.SubjectInUse, LinkLoginOutcome.SubjectInUse)]
    public async Task Reports_what_the_link_found(LinkLoginResult result, LinkLoginOutcome expected)
    {
        var repository = Substitute.For<IPersonRepository>();
        repository.LinkLoginAsync(PersonTestData.Id, "kc-1", Arg.Any<CancellationToken>()).Returns(result);
        var handler = new LinkLoginHandler(repository);

        var outcome = await handler.HandleAsync(
            new LinkLoginCommand(PersonTestData.Id, "kc-1"), TestContext.Current.CancellationToken);

        outcome.Should().Be(expected);
    }
}
