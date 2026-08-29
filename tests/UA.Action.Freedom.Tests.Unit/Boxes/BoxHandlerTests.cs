using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Unit.Boxes;

/// <summary>
/// Boxes, and the one moment that matters in their life: validation.
/// </summary>
/// <remarks>
/// A Loader physically checks the contents and weighs the box. That check is the trust boundary
/// between the donor and Ukrainian Action, and the weight it produces is what the border check
/// relies on (docs/domain/key-concepts.md § Box). These tests pin what validation freezes and
/// why: once a box is validated, nothing that would make the confirmed weight a lie is allowed
/// through.
/// </remarks>
public class BoxHandlerTests
{
    private const int BoxId = 7;

    private static readonly Guid Loader = new("2b9c1e40-7d8a-4c31-9f52-6a0b8d3e5c11");

    private static BoxReadModel ABox(bool validated = false) => new(
        BoxId,
        WeightKg: validated ? 24 : 0,
        ReceiverRef: null,
        House: "Unit 4",
        Street: "Cross Road",
        City: "Coventry",
        Country: "United Kingdom",
        Postcode: "CV1 2AB",
        ValidatedByPersonId: validated ? Loader : null,
        ValidatedAt: validated ? new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc) : null);

    private static IPersonRepository AKnownLoader()
    {
        var people = Substitute.For<IPersonRepository>();
        people.ExistsAsync(Loader, Arg.Any<CancellationToken>()).Returns(true);
        return people;
    }

    [Fact]
    public async Task A_new_box_starts_with_no_confirmed_weight()
    {
        // Zero rather than an estimate: an unverified weight on a border document would be a
        // guess presented as a fact.
        var repository = Substitute.For<IBoxRepository>();
        var handler = new CreateBoxHandler(repository);

        await handler.HandleAsync(
            new CreateBoxCommand(null, "Unit 4", "Cross Road", "Coventry", "United Kingdom", "CV1 2AB"),
            CancellationToken.None);

        await repository.Received(1).AddAsync(
            Arg.Is<BoxReadModel>(box => box.WeightKg == 0 && !box.Validated),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_loader_validates_a_box_and_confirms_its_weight()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.ValidateAsync(BoxId, Loader, 24, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = new ValidateBoxHandler(repository, AKnownLoader());

        var outcome = await handler.HandleAsync(
            new ValidateBoxCommand(BoxId, Loader, 24), CancellationToken.None);

        outcome.Should().Be(ValidateBoxOutcome.Validated);
    }

    [Fact]
    public async Task Refuses_to_validate_a_box_twice()
    {
        // Re-validating would overwrite the record of who checked it and when, which is the
        // audit artefact the whole exercise exists to produce.
        var repository = Substitute.For<IBoxRepository>();
        repository.ValidateAsync(BoxId, Loader, 24, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox(validated: true));
        var handler = new ValidateBoxHandler(repository, AKnownLoader());

        var outcome = await handler.HandleAsync(
            new ValidateBoxCommand(BoxId, Loader, 24), CancellationToken.None);

        outcome.Should().Be(ValidateBoxOutcome.AlreadyValidated);
    }

    [Fact]
    public async Task Reports_not_found_when_validating_a_box_that_does_not_exist()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.ValidateAsync(BoxId, Loader, 24, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns((BoxReadModel?)null);
        var handler = new ValidateBoxHandler(repository, AKnownLoader());

        var outcome = await handler.HandleAsync(
            new ValidateBoxCommand(BoxId, Loader, 24), CancellationToken.None);

        outcome.Should().Be(ValidateBoxOutcome.NotFound);
    }

    [Fact]
    public async Task Refuses_to_record_a_validator_who_is_not_a_volunteer_on_file()
    {
        // A signature naming somebody who does not exist is worse than no signature, because it
        // looks like accountability.
        var repository = Substitute.For<IBoxRepository>();
        var people = Substitute.For<IPersonRepository>();
        people.ExistsAsync(Loader, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new ValidateBoxHandler(repository, people);

        var outcome = await handler.HandleAsync(
            new ValidateBoxCommand(BoxId, Loader, 24), CancellationToken.None);

        outcome.Should().Be(ValidateBoxOutcome.NoSuchValidator);
        await repository.DidNotReceive().ValidateAsync(
            Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_can_be_packed_into_a_validated_box()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox(validated: true));
        var handler = new AddBoxItemHandler(repository);

        var outcome = await handler.HandleAsync(
            new AddBoxItemCommand(BoxId, "Blankets", new Dictionary<string, string>()), CancellationToken.None);

        outcome.Should().Be(AddBoxItemOutcome.AlreadyValidated);
        await repository.DidNotReceive().AddItemAsync(
            Arg.Any<int>(), Arg.Any<BoxItemReadModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_can_be_taken_out_of_a_validated_box_either()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox(validated: true));
        var handler = new RemoveBoxItemHandler(repository);

        var outcome = await handler.HandleAsync(
            new RemoveBoxItemCommand(BoxId, Guid.NewGuid()), CancellationToken.None);

        outcome.Should().Be(RemoveBoxItemOutcome.AlreadyValidated);
        await repository.DidNotReceive().DeleteItemAsync(
            Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Packing_an_item_into_an_open_box_mints_it_an_identifier()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        var handler = new AddBoxItemHandler(repository);

        var outcome = await handler.HandleAsync(
            new AddBoxItemCommand(BoxId, "Blankets", new Dictionary<string, string> { ["size"] = "double" }),
            CancellationToken.None);

        outcome.Should().Be(AddBoxItemOutcome.Added);
        await repository.Received(1).AddItemAsync(
            BoxId,
            Arg.Is<BoxItemReadModel>(item =>
                item.Id != Guid.Empty
                && item.Description == "Blankets"
                && item.Properties["size"] == "double"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_validated_box_cannot_be_pointed_at_a_different_receiver()
    {
        // The Loader signed for this box going to this receiver.
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox(validated: true));
        var handler = new UpdateBoxHandler(repository);

        var outcome = await handler.HandleAsync(
            new UpdateBoxCommand(BoxId, Guid.NewGuid(), null, null, "Lviv", "Ukraine", null),
            CancellationToken.None);

        outcome.Should().Be(UpdateBoxOutcome.AlreadyValidated);
        await repository.DidNotReceive().UpdateAsync(Arg.Any<BoxReadModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Updating_an_open_box_leaves_its_validation_record_alone()
    {
        // The update carries no weight and no validation fields, so there is no way to forge a
        // validation by sending an ordinary edit.
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        repository.UpdateAsync(Arg.Any<BoxReadModel>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new UpdateBoxHandler(repository);

        await handler.HandleAsync(
            new UpdateBoxCommand(BoxId, null, null, null, "Dover", "United Kingdom", "CT16 1JA"),
            CancellationToken.None);

        await repository.Received(1).UpdateAsync(
            Arg.Is<BoxReadModel>(box => box.City == "Dover" && !box.Validated && box.WeightKg == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Listing_the_contents_of_an_unknown_box_returns_nothing()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.ExistsAsync(BoxId, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new ListBoxItemsHandler(repository);

        var items = await handler.HandleAsync(new ListBoxItemsQuery(BoxId), CancellationToken.None);

        items.Should().BeNull();
    }

    [Fact]
    public async Task List_clamps_a_nonsense_page_and_page_size_to_the_defaults()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<BoxReadModel>());
        var handler = new ListBoxesHandler(repository);

        await handler.HandleAsync(new ListBoxesQuery(Page: 0, PageSize: 100_000), CancellationToken.None);

        await repository.Received(1).ListAsync(1, 50, Arg.Any<CancellationToken>());
    }
}
