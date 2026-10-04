using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Donations;
using UA.Action.Freedom.Application.Categories;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Domain;

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

    private const int LocationId = 3;

    private const int CategoryId = 4;

    private static readonly Guid Loader = new("2b9c1e40-7d8a-4c31-9f52-6a0b8d3e5c11");

    private static BoxReadModel ABox(bool validated = false) => new(
        BoxId,
        WeightKg: validated ? 24 : 0,
        WidthCm: validated ? 40 : null,
        DepthCm: validated ? 30 : null,
        HeightCm: validated ? 20 : null,
        ReceiverRef: null,
        LocationId: LocationId,
        ValidatedByPersonId: validated ? Loader : null,
        ValidatedAt: validated ? new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc) : null);

    [Fact]
    public async Task A_new_box_starts_with_no_confirmed_weight()
    {
        // Zero rather than an estimate: an unverified weight on a border document would be a
        // guess presented as a fact.
        var repository = Substitute.For<IBoxRepository>();
        var handler = new CreateBoxHandler(repository, Substitute.For<IReceiverRepository>());

        await handler.HandleAsync(
            new CreateBoxCommand(null, LocationId),
            CancellationToken.None);

        await repository.Received(1).AddAsync(
            Arg.Is<BoxReadModel>(box => box.WeightKg == 0 && !box.Validated),
            Arg.Any<CancellationToken>());
    }

    private static ItemCategoryReadModel ACategory(
        bool isNotCarried = false, int? warnWithinDays = null) => new(
        CategoryId, "Medicine", "", IsFixed: true, HazardClass: null, IsSensitive: false, isNotCarried, warnWithinDays);

    private static IItemCategoryRepository ACategoryRepository(ItemCategoryReadModel? category = null)
    {
        var categories = Substitute.For<IItemCategoryRepository>();
        var known = category ?? ACategory();
        categories.GetByIdAsync(known.Id, Arg.Any<CancellationToken>()).Returns(known);
        categories.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<ItemCategoryReadModel> { known });
        return categories;
    }

    private static IReceiverRepository AReceiverRepository(Guid receiverRef, ReceiverStatus? status)
    {
        var receivers = Substitute.For<IReceiverRepository>();
        receivers.GetByRefAsync(receiverRef, Arg.Any<CancellationToken>()).Returns(
            status is { } found ? new ReceiverReadModel(receiverRef, "Kharkiv Regional Hospital", "Kharkiv oblast", found) : null);
        return receivers;
    }

    [Fact]
    public async Task A_box_can_be_created_for_a_registered_receiver()
    {
        var receiverRef = Guid.NewGuid();
        var repository = Substitute.For<IBoxRepository>();
        repository.AddAsync(Arg.Any<BoxReadModel>(), Arg.Any<CancellationToken>()).Returns(7);
        var handler = new CreateBoxHandler(repository, AReceiverRepository(receiverRef, ReceiverStatus.Registered));

        var result = await handler.HandleAsync(new CreateBoxCommand(receiverRef, LocationId), CancellationToken.None);

        result.Should().Be(new CreateBoxResult(CreateBoxOutcome.Created, 7));
    }

    [Theory]
    [InlineData(ReceiverStatus.Pending)]
    [InlineData(ReceiverStatus.Suspended)]
    [InlineData(ReceiverStatus.Expired)]
    public async Task A_box_cannot_be_created_for_a_receiver_that_is_not_registered(ReceiverStatus status)
    {
        var receiverRef = Guid.NewGuid();
        var repository = Substitute.For<IBoxRepository>();
        var handler = new CreateBoxHandler(repository, AReceiverRepository(receiverRef, status));

        var result = await handler.HandleAsync(new CreateBoxCommand(receiverRef, LocationId), CancellationToken.None);

        result.Outcome.Should().Be(CreateBoxOutcome.ReceiverNotRegistered);
        await repository.DidNotReceive().AddAsync(Arg.Any<BoxReadModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_box_cannot_be_created_for_a_receiver_that_does_not_exist()
    {
        var receiverRef = Guid.NewGuid();
        var repository = Substitute.For<IBoxRepository>();
        var handler = new CreateBoxHandler(repository, AReceiverRepository(receiverRef, status: null));

        var result = await handler.HandleAsync(new CreateBoxCommand(receiverRef, LocationId), CancellationToken.None);

        result.Outcome.Should().Be(CreateBoxOutcome.ReceiverNotFound);
        await repository.DidNotReceive().AddAsync(Arg.Any<BoxReadModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_box_with_no_destination_yet_needs_no_receiver()
    {
        var receivers = Substitute.For<IReceiverRepository>();
        var repository = Substitute.For<IBoxRepository>();
        repository.AddAsync(Arg.Any<BoxReadModel>(), Arg.Any<CancellationToken>()).Returns(3);
        var handler = new CreateBoxHandler(repository, receivers);

        var result = await handler.HandleAsync(new CreateBoxCommand(null, LocationId), CancellationToken.None);

        result.Outcome.Should().Be(CreateBoxOutcome.Created);
        await receivers.DidNotReceive().GetByRefAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_open_box_can_be_pointed_at_a_registered_receiver()
    {
        var receiverRef = Guid.NewGuid();
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        repository.UpdateAsync(Arg.Any<BoxReadModel>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new UpdateBoxHandler(repository, AReceiverRepository(receiverRef, ReceiverStatus.Registered));

        var outcome = await handler.HandleAsync(new UpdateBoxCommand(BoxId, receiverRef, LocationId), CancellationToken.None);

        outcome.Should().Be(UpdateBoxOutcome.Updated);
    }

    [Fact]
    public async Task An_open_box_cannot_be_pointed_at_a_receiver_that_is_not_registered()
    {
        var receiverRef = Guid.NewGuid();
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        var handler = new UpdateBoxHandler(repository, AReceiverRepository(receiverRef, ReceiverStatus.Pending));

        var outcome = await handler.HandleAsync(new UpdateBoxCommand(BoxId, receiverRef, LocationId), CancellationToken.None);

        outcome.Should().Be(UpdateBoxOutcome.ReceiverNotRegistered);
        await repository.DidNotReceive().UpdateAsync(Arg.Any<BoxReadModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_open_box_cannot_be_pointed_at_a_receiver_that_does_not_exist()
    {
        var receiverRef = Guid.NewGuid();
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        var handler = new UpdateBoxHandler(repository, AReceiverRepository(receiverRef, status: null));

        var outcome = await handler.HandleAsync(new UpdateBoxCommand(BoxId, receiverRef, LocationId), CancellationToken.None);

        outcome.Should().Be(UpdateBoxOutcome.ReceiverNotFound);
    }

    [Fact]
    public async Task A_box_can_still_be_moved_when_its_receiver_has_since_been_suspended()
    {
        // Not a new allocation: the receiver is unchanged, so only the location is being edited.
        var receiverRef = Guid.NewGuid();
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox() with { ReceiverRef = receiverRef });
        repository.UpdateAsync(Arg.Any<BoxReadModel>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new UpdateBoxHandler(repository, AReceiverRepository(receiverRef, ReceiverStatus.Suspended));

        var outcome = await handler.HandleAsync(new UpdateBoxCommand(BoxId, receiverRef, 9), CancellationToken.None);

        outcome.Should().Be(UpdateBoxOutcome.Updated);
    }

    [Fact]
    public async Task A_loader_validates_a_box_and_confirms_its_weight()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.ValidateAsync(
                BoxId, Loader, 24, Arg.Any<decimal?>(), Arg.Any<decimal?>(), Arg.Any<decimal?>(),
                Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = new ValidateBoxHandler(repository);

        var outcome = await handler.HandleAsync(
            new ValidateBoxCommand(BoxId, Loader, 24), CancellationToken.None);

        outcome.Should().Be(ValidateBoxOutcome.Validated);
    }

    [Fact]
    public async Task A_loader_validates_a_box_and_records_its_dimensions()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.ValidateAsync(
                BoxId, Loader, 24, 40m, 30m, 20m, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = new ValidateBoxHandler(repository);

        var outcome = await handler.HandleAsync(
            new ValidateBoxCommand(BoxId, Loader, 24, WidthCm: 40m, DepthCm: 30m, HeightCm: 20m),
            CancellationToken.None);

        outcome.Should().Be(ValidateBoxOutcome.Validated);
        await repository.Received(1).ValidateAsync(
            BoxId, Loader, 24, 40m, 30m, 20m, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_to_validate_a_box_twice()
    {
        // Re-validating would overwrite the record of who checked it and when, which is the
        // audit artefact the whole exercise exists to produce.
        var repository = Substitute.For<IBoxRepository>();
        repository.ValidateAsync(
                BoxId, Loader, 24, Arg.Any<decimal?>(), Arg.Any<decimal?>(), Arg.Any<decimal?>(),
                Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox(validated: true));
        var handler = new ValidateBoxHandler(repository);

        var outcome = await handler.HandleAsync(
            new ValidateBoxCommand(BoxId, Loader, 24), CancellationToken.None);

        outcome.Should().Be(ValidateBoxOutcome.AlreadyValidated);
    }

    [Fact]
    public async Task Reports_not_found_when_validating_a_box_that_does_not_exist()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.ValidateAsync(
                BoxId, Loader, 24, Arg.Any<decimal?>(), Arg.Any<decimal?>(), Arg.Any<decimal?>(),
                Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns((BoxReadModel?)null);
        var handler = new ValidateBoxHandler(repository);

        var outcome = await handler.HandleAsync(
            new ValidateBoxCommand(BoxId, Loader, 24), CancellationToken.None);

        outcome.Should().Be(ValidateBoxOutcome.NotFound);
    }

    [Fact]
    public async Task Nothing_can_be_packed_into_a_validated_box()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox(validated: true));
        var handler = new AddBoxItemHandler(repository, ACategoryRepository(), Substitute.For<IDonationRepository>());

        var result = await handler.HandleAsync(
            new AddBoxItemCommand(BoxId, "Blankets", new Dictionary<string, string>(), CategoryId), CancellationToken.None);

        result.Outcome.Should().Be(AddBoxItemOutcome.AlreadyValidated);
        await repository.DidNotReceive().AddItemAsync(
            Arg.Any<int>(), Arg.Any<BoxItemReadModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_validated_box_cannot_be_deleted()
    {
        // The attestation is a commitment made at border checkpoints. Deleting the box would erase who vouched
        // for what, so a box whose contents must change is replaced instead (ADR 0011).
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox(validated: true));
        var handler = new DeleteBoxHandler(repository);

        var outcome = await handler.HandleAsync(new DeleteBoxCommand(BoxId), CancellationToken.None);

        outcome.Should().Be(DeleteBoxOutcome.AlreadyValidated);
        await repository.DidNotReceive().DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_box_nobody_has_validated_can_still_be_deleted()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        repository.DeleteAsync(BoxId, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new DeleteBoxHandler(repository);

        var outcome = await handler.HandleAsync(new DeleteBoxCommand(BoxId), CancellationToken.None);

        outcome.Should().Be(DeleteBoxOutcome.Deleted);
    }

    [Fact]
    public async Task Deleting_a_box_that_does_not_exist_is_not_found()
    {
        var handler = new DeleteBoxHandler(Substitute.For<IBoxRepository>());

        var outcome = await handler.HandleAsync(new DeleteBoxCommand(BoxId), CancellationToken.None);

        outcome.Should().Be(DeleteBoxOutcome.NotFound);
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
    public async Task An_item_can_name_the_donation_it_came_in()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        var donations = Substitute.For<IDonationRepository>();
        donations.GetByIdAsync(41, Arg.Any<CancellationToken>())
            .Returns(new DonationReadModel(41, Guid.NewGuid(), "Margaret Hollis", new DateOnly(2026, 9, 20), null));
        var handler = new AddBoxItemHandler(repository, ACategoryRepository(), donations);

        var result = await handler.HandleAsync(
            new AddBoxItemCommand(BoxId, "Blankets", new Dictionary<string, string>(), CategoryId, DonationId: 41),
            CancellationToken.None);

        result.Outcome.Should().Be(AddBoxItemOutcome.Added);
        await repository.Received(1).AddItemAsync(
            BoxId, Arg.Is<BoxItemReadModel>(item => item.DonationId == 41), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_item_cannot_name_a_donation_that_does_not_exist()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        var handler = new AddBoxItemHandler(repository, ACategoryRepository(), Substitute.For<IDonationRepository>());

        var result = await handler.HandleAsync(
            new AddBoxItemCommand(BoxId, "Blankets", new Dictionary<string, string>(), CategoryId, DonationId: 41),
            CancellationToken.None);

        result.Outcome.Should().Be(AddBoxItemOutcome.DonationNotFound);
        await repository.DidNotReceive().AddItemAsync(
            Arg.Any<int>(), Arg.Any<BoxItemReadModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Packing_an_item_into_an_open_box_mints_it_an_identifier()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        var handler = new AddBoxItemHandler(repository, ACategoryRepository(), Substitute.For<IDonationRepository>());

        var result = await handler.HandleAsync(
            new AddBoxItemCommand(BoxId, "Blankets", new Dictionary<string, string> { ["size"] = "double" }, CategoryId),
            CancellationToken.None);

        result.Outcome.Should().Be(AddBoxItemOutcome.Added);
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
        var handler = new UpdateBoxHandler(repository, Substitute.For<IReceiverRepository>());

        var outcome = await handler.HandleAsync(
            new UpdateBoxCommand(BoxId, Guid.NewGuid(), LocationId),
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
        var handler = new UpdateBoxHandler(repository, Substitute.For<IReceiverRepository>());

        const int newLocationId = 9;

        await handler.HandleAsync(
            new UpdateBoxCommand(BoxId, null, newLocationId),
            CancellationToken.None);

        await repository.Received(1).UpdateAsync(
            Arg.Is<BoxReadModel>(box => box.LocationId == newLocationId && !box.Validated && box.WeightKg == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Listing_the_contents_of_an_unknown_box_returns_nothing()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.ExistsAsync(BoxId, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new ListBoxItemsHandler(repository, ACategoryRepository());

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

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static BoxItemReadModel AnItem(DateOnly? expiresOn = null) => new(
        Guid.NewGuid(), "Paracetamol", new Dictionary<string, string>(), CategoryId, ExpiresOn: expiresOn);

    [Fact]
    public async Task An_item_cannot_be_packed_under_a_category_that_does_not_exist()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        var categories = Substitute.For<IItemCategoryRepository>();
        var handler = new AddBoxItemHandler(repository, categories, Substitute.For<IDonationRepository>());

        var result = await handler.HandleAsync(
            new AddBoxItemCommand(BoxId, "Gas canister", new Dictionary<string, string>(), CategoryId),
            CancellationToken.None);

        result.Outcome.Should().Be(AddBoxItemOutcome.CategoryNotFound);
        await repository.DidNotReceive().AddItemAsync(
            Arg.Any<int>(), Arg.Any<BoxItemReadModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_item_the_convoy_will_not_carry_is_packed_with_a_warning()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        var handler = new AddBoxItemHandler(repository, ACategoryRepository(ACategory(isNotCarried: true)), Substitute.For<IDonationRepository>());

        var result = await handler.HandleAsync(
            new AddBoxItemCommand(BoxId, "Gas canister", new Dictionary<string, string>(), CategoryId),
            CancellationToken.None);

        result.Outcome.Should().Be(AddBoxItemOutcome.Added);
        result.Warnings.Should().Equal(ItemWarning.NotCarried);
        await repository.Received(1).AddItemAsync(
            BoxId, Arg.Is<BoxItemReadModel>(item => item.Id == result.ItemId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_already_expired_item_is_packed_so_the_loader_can_see_it_but_it_warns()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        var handler = new AddBoxItemHandler(repository, ACategoryRepository(ACategory(warnWithinDays: 180)), Substitute.For<IDonationRepository>());

        var result = await handler.HandleAsync(
            new AddBoxItemCommand(
                BoxId, "Paracetamol", new Dictionary<string, string>(), CategoryId, ExpiresOn: Today.AddDays(-1)),
            CancellationToken.None);

        result.Outcome.Should().Be(AddBoxItemOutcome.Added);
        result.Warnings.Should().Equal(ItemWarning.Expired);
    }

    [Fact]
    public async Task A_short_dated_item_warns_by_its_categorys_own_threshold()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        var handler = new AddBoxItemHandler(repository, ACategoryRepository(ACategory(warnWithinDays: 180)), Substitute.For<IDonationRepository>());

        var short_ = await handler.HandleAsync(
            new AddBoxItemCommand(
                BoxId, "Paracetamol", new Dictionary<string, string>(), CategoryId, ExpiresOn: Today.AddDays(100)),
            CancellationToken.None);
        var fine = await handler.HandleAsync(
            new AddBoxItemCommand(
                BoxId, "Paracetamol", new Dictionary<string, string>(), CategoryId, ExpiresOn: Today.AddDays(400)),
            CancellationToken.None);

        short_.Warnings.Should().Equal(ItemWarning.ShortShelfLife);
        fine.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task A_box_holding_an_expired_item_cannot_be_validated()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        repository.ListItemsAsync(BoxId, Arg.Any<CancellationToken>())
            .Returns(new List<BoxItemReadModel> { AnItem(Today.AddDays(30)), AnItem(Today.AddDays(-1)) });
        var handler = new ValidateBoxHandler(repository);

        var outcome = await handler.HandleAsync(new ValidateBoxCommand(BoxId, Loader, 24), CancellationToken.None);

        outcome.Should().Be(ValidateBoxOutcome.HasExpiredItems);
        await repository.DidNotReceive().ValidateAsync(
            Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<decimal?>(), Arg.Any<decimal?>(),
            Arg.Any<decimal?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_item_that_expires_today_does_not_block_validation()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.ListItemsAsync(BoxId, Arg.Any<CancellationToken>())
            .Returns(new List<BoxItemReadModel> { AnItem(Today), AnItem() });
        repository.ValidateAsync(
                BoxId, Loader, 24, Arg.Any<decimal?>(), Arg.Any<decimal?>(), Arg.Any<decimal?>(),
                Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = new ValidateBoxHandler(repository);

        var outcome = await handler.HandleAsync(new ValidateBoxCommand(BoxId, Loader, 24), CancellationToken.None);

        outcome.Should().Be(ValidateBoxOutcome.Validated);
    }

    [Fact]
    public async Task A_box_already_validated_says_so_even_if_an_item_has_since_expired()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox(validated: true));
        repository.ListItemsAsync(BoxId, Arg.Any<CancellationToken>())
            .Returns(new List<BoxItemReadModel> { AnItem(Today.AddDays(-1)) });
        var handler = new ValidateBoxHandler(repository);

        var outcome = await handler.HandleAsync(new ValidateBoxCommand(BoxId, Loader, 24), CancellationToken.None);

        outcome.Should().Be(ValidateBoxOutcome.AlreadyValidated);
    }

    [Fact]
    public async Task Listing_the_contents_reads_each_item_with_what_its_category_says_about_it()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.ExistsAsync(BoxId, Arg.Any<CancellationToken>()).Returns(true);
        repository.ListItemsAsync(BoxId, Arg.Any<CancellationToken>())
            .Returns(new List<BoxItemReadModel> { AnItem(Today.AddDays(-2)), AnItem(Today.AddDays(30)), AnItem() });
        var handler = new ListBoxItemsHandler(repository, ACategoryRepository(ACategory(warnWithinDays: 180, isNotCarried: true)));

        var items = await handler.HandleAsync(new ListBoxItemsQuery(BoxId), CancellationToken.None);

        items!.Select(item => item.ShelfLife).Should().Equal(
            ShelfLifeStatus.Expired, ShelfLifeStatus.Short, ShelfLifeStatus.Fine);
        items.Should().OnlyContain(item => item.CategoryNameEn == "Medicine" && item.IsNotCarried);
    }
}
