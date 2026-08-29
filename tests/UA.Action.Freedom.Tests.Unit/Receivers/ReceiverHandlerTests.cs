using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Receivers;

namespace UA.Action.Freedom.Tests.Unit.Receivers;

/// <summary>
/// Receivers, and the line drawn through the middle of them.
/// </summary>
/// <remarks>
/// A receiver has a non-sensitive half — reference, organisation, region — that travels on
/// manifests, and a sensitive half holding the Ukrainian delivery address and contact. These
/// tests pin the rules that keep the two apart: the address is never resolved without an audit
/// entry naming who asked, and removing a receiver removes the address with it rather than
/// orphaning it (docs/recommendations.md §4.4).
/// </remarks>
public class ReceiverHandlerTests
{
    private static readonly Guid Ref = new("b3f1c4d2-5a6e-4f70-8901-2c3d4e5f6a7b");

    private static ReceiverDetailReadModel ADetail() => new(
        Ref, "Olena Kovalenko", "+380501234567", "12 Vulytsia Sumska", null, "Kharkiv", "61002", null);

    [Fact]
    public async Task Registering_a_receiver_mints_an_opaque_reference()
    {
        var repository = Substitute.For<IReceiverRepository>();
        var handler = new CreateReceiverHandler(repository);

        var created = await handler.HandleAsync(
            new CreateReceiverCommand("Kharkiv Regional Hospital", "Kharkiv oblast"), CancellationToken.None);

        created.Should().NotBeEmpty();
        await repository.Received(1).AddAsync(
            Arg.Is<ReceiverReadModel>(receiver =>
                receiver.Ref == created
                && receiver.Organisation == "Kharkiv Regional Hospital"
                && receiver.Region == "Kharkiv oblast"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolving_an_address_records_who_asked_and_why()
    {
        // The audit trail matters more than the data it describes (§4.4.3). It is a parameter
        // of the read, so there is no code path that resolves an address without one.
        var repository = Substitute.For<IReceiverDetailRepository>();
        repository.ResolveAsync(Ref, "ground-officer-1", "Delivery scheduled 12 Sept", Arg.Any<CancellationToken>())
            .Returns(ADetail());
        var handler = new GetReceiverDetailHandler(repository);

        var detail = await handler.HandleAsync(
            new GetReceiverDetailQuery(Ref, "ground-officer-1", "Delivery scheduled 12 Sept"),
            CancellationToken.None);

        detail!.City.Should().Be("Kharkiv");
        await repository.Received(1).ResolveAsync(
            Ref, "ground-officer-1", "Delivery scheduled 12 Sept", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolving_an_address_that_was_never_recorded_returns_nothing()
    {
        var repository = Substitute.For<IReceiverDetailRepository>();
        repository.ResolveAsync(Ref, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((ReceiverDetailReadModel?)null);
        var handler = new GetReceiverDetailHandler(repository);

        var detail = await handler.HandleAsync(
            new GetReceiverDetailQuery(Ref, "ground-officer-1", null), CancellationToken.None);

        detail.Should().BeNull();
    }

    [Fact]
    public async Task Recording_delivery_detail_needs_the_receiver_to_exist_first()
    {
        var receivers = Substitute.For<IReceiverRepository>();
        receivers.ExistsAsync(Ref, Arg.Any<CancellationToken>()).Returns(false);
        var detail = Substitute.For<IReceiverDetailRepository>();
        var handler = new SetReceiverDetailHandler(receivers, detail);

        var outcome = await handler.HandleAsync(
            new SetReceiverDetailCommand(
                Ref, "Olena Kovalenko", "+380501234567", "12 Vulytsia Sumska", null, "Kharkiv", "61002", null),
            CancellationToken.None);

        outcome.Should().Be(SetReceiverDetailOutcome.ReceiverNotFound);
        await detail.DidNotReceive().UpsertAsync(
            Arg.Any<ReceiverDetailReadModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recording_delivery_detail_stores_it_against_the_receiver()
    {
        var receivers = Substitute.For<IReceiverRepository>();
        receivers.ExistsAsync(Ref, Arg.Any<CancellationToken>()).Returns(true);
        var detail = Substitute.For<IReceiverDetailRepository>();
        var handler = new SetReceiverDetailHandler(receivers, detail);

        var outcome = await handler.HandleAsync(
            new SetReceiverDetailCommand(
                Ref, "Olena Kovalenko", "+380501234567", "12 Vulytsia Sumska", null, "Kharkiv", "61002", null),
            CancellationToken.None);

        outcome.Should().Be(SetReceiverDetailOutcome.Set);
        await detail.Received(1).UpsertAsync(
            Arg.Is<ReceiverDetailReadModel>(stored => stored.Ref == Ref && stored.City == "Kharkiv"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removing_a_receiver_removes_its_delivery_address_too()
    {
        // Deleting the reference while keeping the address is the worst outcome available:
        // the data is still held, and nothing points at it to say whose it is (§4.4.5).
        var receivers = Substitute.For<IReceiverRepository>();
        receivers.DeleteAsync(Ref, Arg.Any<CancellationToken>()).Returns(true);
        var detail = Substitute.For<IReceiverDetailRepository>();
        var handler = new DeleteReceiverHandler(receivers, detail);

        var outcome = await handler.HandleAsync(new DeleteReceiverCommand(Ref), CancellationToken.None);

        outcome.Should().Be(DeleteReceiverOutcome.Deleted);
        await detail.Received(1).DeleteAsync(Ref, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removing_an_unknown_receiver_reports_not_found()
    {
        var receivers = Substitute.For<IReceiverRepository>();
        receivers.DeleteAsync(Ref, Arg.Any<CancellationToken>()).Returns(false);
        var detail = Substitute.For<IReceiverDetailRepository>();
        var handler = new DeleteReceiverHandler(receivers, detail);

        var outcome = await handler.HandleAsync(new DeleteReceiverCommand(Ref), CancellationToken.None);

        outcome.Should().Be(DeleteReceiverOutcome.NotFound);
    }

    [Fact]
    public async Task List_clamps_a_nonsense_page_and_page_size_to_the_defaults()
    {
        var repository = Substitute.For<IReceiverRepository>();
        repository.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<ReceiverReadModel>());
        var handler = new ListReceiversHandler(repository);

        await handler.HandleAsync(new ListReceiversQuery(Page: 0, PageSize: 100_000), CancellationToken.None);

        await repository.Received(1).ListAsync(1, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_receiver_read_model_carries_no_address_or_contact()
    {
        // Structural, not incidental. Code holding only a ReceiverReadModel has nothing
        // sensitive to leak, which is what makes redaction hold in document generation and in
        // logging without either of them having to remember a rule.
        var properties = typeof(ReceiverReadModel).GetProperties().Select(property => property.Name);

        properties.Should().BeEquivalentTo("Ref", "Organisation", "Region");
    }
}
