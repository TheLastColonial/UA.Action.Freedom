using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// Creating, changing and removing a convoy. The identifier is assigned by the database, so the
/// create handler hands back what the insert returned rather than minting one.
/// </summary>
public class ConvoyHandlerTests
{
    [Fact]
    public async Task Creating_a_convoy_returns_the_identifier_the_database_assigned()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.AddAsync(ConvoyTestData.Start, ConvoyTestData.ExpectedEnd, Arg.Any<CancellationToken>())
            .Returns(ConvoyTestData.Id);
        var handler = new CreateConvoyHandler(repository);

        var id = await handler.HandleAsync(ConvoyTestData.ACreateCommand(), CancellationToken.None);

        id.Should().Be(ConvoyTestData.Id);
    }

    [Fact]
    public async Task Reports_updated_when_a_row_was_changed()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.UpdateAsync(Arg.Any<ConvoyReadModel>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new UpdateConvoyHandler(repository);

        var outcome = await handler.HandleAsync(ConvoyTestData.AnUpdateCommand(), CancellationToken.None);

        outcome.Should().Be(UpdateConvoyOutcome.Updated);
    }

    [Fact]
    public async Task Reports_not_found_when_updating_a_convoy_that_does_not_exist()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.UpdateAsync(Arg.Any<ConvoyReadModel>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = new UpdateConvoyHandler(repository);

        var outcome = await handler.HandleAsync(ConvoyTestData.AnUpdateCommand(), CancellationToken.None);

        outcome.Should().Be(UpdateConvoyOutcome.NotFound);
    }

    [Fact]
    public async Task Reports_deleted_when_a_row_was_removed()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.DeleteAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new DeleteConvoyHandler(repository);

        var outcome = await handler.HandleAsync(new DeleteConvoyCommand(ConvoyTestData.Id), CancellationToken.None);

        outcome.Should().Be(DeleteConvoyOutcome.Deleted);
    }

    [Fact]
    public async Task Reports_not_found_when_deleting_a_convoy_that_does_not_exist()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.DeleteAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new DeleteConvoyHandler(repository);

        var outcome = await handler.HandleAsync(new DeleteConvoyCommand(ConvoyTestData.Id), CancellationToken.None);

        outcome.Should().Be(DeleteConvoyOutcome.NotFound);
    }

    [Fact]
    public async Task List_clamps_a_nonsense_page_and_page_size_to_the_defaults()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<ConvoyReadModel> { ConvoyTestData.AReadModel() });
        var handler = new ListConvoysHandler(repository);

        await handler.HandleAsync(new ListConvoysQuery(Page: 0, PageSize: 100_000), CancellationToken.None);

        await repository.Received(1).ListAsync(1, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_returns_nothing_when_there_is_no_such_convoy()
    {
        var repository = Substitute.For<IConvoyRepository>();
        repository.GetByIdAsync(ConvoyTestData.Id, Arg.Any<CancellationToken>())
            .Returns((ConvoyReadModel?)null);
        var handler = new GetConvoyByIdHandler(repository);

        var convoy = await handler.HandleAsync(new GetConvoyByIdQuery(ConvoyTestData.Id), CancellationToken.None);

        convoy.Should().BeNull();
    }
}
