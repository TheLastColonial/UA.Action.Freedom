using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Categories;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Categories;

/// <summary>
/// Categories carry what a border needs to know about a kind of item. The rules worth a handler: a name is
/// unique, a category created through the API is never fixed, and a code can be cleared.
/// </summary>
public class CategoryHandlerTests
{
    private static CreateCategoryCommand ACreateCommand() => new(
        "Bedding", "", HazardClass: null, IsSensitive: false, IsNotCarried: false, WarnWithinDays: null);

    [Fact]
    public async Task Creating_a_category_with_a_free_name_returns_its_identifier()
    {
        var repository = Substitute.For<IItemCategoryRepository>();
        repository.AddAsync(Arg.Any<ItemCategoryReadModel>(), Arg.Any<CancellationToken>()).Returns(7);
        var handler = new CreateCategoryHandler(repository);

        var result = await handler.HandleAsync(ACreateCommand(), CancellationToken.None);

        result.Outcome.Should().Be(CreateCategoryOutcome.Created);
        result.Id.Should().Be(7);
    }

    [Fact]
    public async Task A_category_created_through_the_handler_is_never_fixed()
    {
        var repository = Substitute.For<IItemCategoryRepository>();
        repository.AddAsync(Arg.Any<ItemCategoryReadModel>(), Arg.Any<CancellationToken>()).Returns(7);
        var handler = new CreateCategoryHandler(repository);

        await handler.HandleAsync(ACreateCommand(), CancellationToken.None);

        await repository.Received(1).AddAsync(
            Arg.Is<ItemCategoryReadModel>(category => !category.IsFixed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_name_already_taken_is_reported_not_thrown()
    {
        var repository = Substitute.For<IItemCategoryRepository>();
        repository.AddAsync(Arg.Any<ItemCategoryReadModel>(), Arg.Any<CancellationToken>()).Returns((int?)null);
        var handler = new CreateCategoryHandler(repository);

        var result = await handler.HandleAsync(ACreateCommand(), CancellationToken.None);

        result.Outcome.Should().Be(CreateCategoryOutcome.NameTaken);
    }

    [Theory]
    [InlineData(true, UpdateCategoryOutcome.Updated)]
    [InlineData(false, UpdateCategoryOutcome.NotFound)]
    [InlineData(null, UpdateCategoryOutcome.NameTaken)]
    public async Task An_update_maps_the_repositorys_answer_to_an_outcome(bool? stored, UpdateCategoryOutcome expected)
    {
        var repository = Substitute.For<IItemCategoryRepository>();
        repository.UpdateAsync(Arg.Any<ItemCategoryReadModel>(), Arg.Any<CancellationToken>()).Returns(stored);
        var handler = new UpdateCategoryHandler(repository);

        var outcome = await handler.HandleAsync(
            new UpdateCategoryCommand(2, "Bedding", "", null, false, false, null), CancellationToken.None);

        outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData(true, SetCategoryCodeOutcome.Set)]
    [InlineData(false, SetCategoryCodeOutcome.NotFound)]
    public async Task Setting_a_code_reports_whether_the_category_exists(bool exists, SetCategoryCodeOutcome expected)
    {
        var repository = Substitute.For<IItemCategoryRepository>();
        repository.SetCodeAsync(2, CustomsAuthority.EU, null, Arg.Any<CancellationToken>()).Returns(exists);
        var handler = new SetCategoryCodeHandler(repository);

        var outcome = await handler.HandleAsync(
            new SetCategoryCodeCommand(2, CustomsAuthority.EU, null), CancellationToken.None);

        outcome.Should().Be(expected);
    }
}
