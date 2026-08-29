using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Unit.People;

/// <summary>
/// Changing a volunteer's details. The handler leans on the repository's affected-row count to
/// tell "changed it" from "no such person" rather than reading first.
/// </summary>
public class UpdatePersonHandlerTests
{
    [Fact]
    public async Task Reports_updated_when_a_row_was_changed()
    {
        var repository = Substitute.For<IPersonRepository>();
        repository.UpdateAsync(Arg.Any<PersonReadModel>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new UpdatePersonHandler(repository);

        var outcome = await handler.HandleAsync(PersonTestData.AnUpdateCommand(), CancellationToken.None);

        outcome.Should().Be(UpdatePersonOutcome.Updated);
        await repository.Received(1).UpdateAsync(
            Arg.Is<PersonReadModel>(person =>
                person.Id == PersonTestData.Id && person.LastName == "Shevchenko-Bell"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_not_found_when_there_was_no_such_person()
    {
        var repository = Substitute.For<IPersonRepository>();
        repository.UpdateAsync(Arg.Any<PersonReadModel>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = new UpdatePersonHandler(repository);

        var outcome = await handler.HandleAsync(PersonTestData.AnUpdateCommand(), CancellationToken.None);

        outcome.Should().Be(UpdatePersonOutcome.NotFound);
    }

    [Fact]
    public async Task Carries_a_commitment_to_the_next_convoy_through_to_the_repository()
    {
        // Whether a driver is committed rather than merely available is what the dispatcher
        // builds driver teams from, so it has to survive the round trip.
        var repository = Substitute.For<IPersonRepository>();
        repository.UpdateAsync(Arg.Any<PersonReadModel>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new UpdatePersonHandler(repository);

        await handler.HandleAsync(
            PersonTestData.AnUpdateCommand(isDriver: true, committed: true),
            CancellationToken.None);

        await repository.Received(1).UpdateAsync(
            Arg.Is<PersonReadModel>(person => person.IsDriver && person.Committed),
            Arg.Any<CancellationToken>());
    }
}
