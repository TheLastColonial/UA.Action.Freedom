using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Declarations;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Declarations;

/// <summary>
/// Recording the ICS2 Entry Summary Declaration a vehicle's crossing was accepted under. Freedom does
/// not submit it (docs/adr/0003): it records the MRN a Ground Officer obtained, write-once.
/// </summary>
public class EnsDeclarationHandlerTests
{
    private const string Mrn = "25FR17551780961AT5";

    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly DateTimeOffset Accepted = new(2026, 8, 24, 9, 30, 0, TimeSpan.Zero);

    private static RecordEnsDeclarationCommand ACommand(string mrn = Mrn) =>
        new(42, Vin, mrn, Accepted, FiledBy: "groundofficer", FilingReference: "STP-2026-0001");

    private static DeclarationReadModel TheEnsDeclaration(
        DeclarationStatus status = DeclarationStatus.Accepted) => new(
        7, 42, Vin, DeclarationKind.Ens, status, null, Mrn, null, null, null, null, null);

    private static IDeclarationRepository ARepository(RecordReferenceResult result = RecordReferenceResult.Recorded)
    {
        var repository = Substitute.For<IDeclarationRepository>();
        repository.RecordReferenceAsync(
                42, Vin, DeclarationKind.Ens, null, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(result);
        repository.GetCurrentAsync(42, Vin, DeclarationKind.Ens, null, Arg.Any<CancellationToken>())
            .Returns(TheEnsDeclaration());
        return repository;
    }

    private static IEnsDeclarationStore AnEmptyStore()
    {
        var store = Substitute.For<IEnsDeclarationStore>();
        store.SaveAsync(Arg.Any<EnsDeclarationReadModel>(), Arg.Any<CancellationToken>()).Returns(true);
        return store;
    }

    [Fact]
    public async Task Records_the_mrn_against_the_vehicle_and_its_detail_against_the_declaration()
    {
        var repository = ARepository();
        var store = AnEmptyStore();

        var outcome = await new RecordEnsDeclarationHandler(repository, store, Substitute.For<IDeclarationSnapshots>()).HandleAsync(
            ACommand(), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordEnsOutcome.Recorded);
        await repository.Received(1).RecordReferenceAsync(
            42, Vin, DeclarationKind.Ens, null, Mrn, Arg.Any<CancellationToken>());
        await store.Received(1).SaveAsync(
            Arg.Is<EnsDeclarationReadModel>(declaration =>
                declaration.DeclarationId == 7
                && declaration.Mrn == Mrn
                && declaration.AcceptedAt == Accepted
                && declaration.FiledBy == "groundofficer"
                && declaration.FilingReference == "STP-2026-0001"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("25fr17551780961at5")]
    public async Task Refuses_an_mrn_that_is_not_shaped_like_one(string mrn)
    {
        var repository = ARepository();

        var outcome = await new RecordEnsDeclarationHandler(repository, AnEmptyStore(), Substitute.For<IDeclarationSnapshots>()).HandleAsync(
            ACommand(mrn), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordEnsOutcome.MalformedMrn);
        await repository.DidNotReceive().RecordReferenceAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<DeclarationKind>(), Arg.Any<Guid?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_a_vehicle_that_is_not_on_the_convoy()
    {
        var store = AnEmptyStore();

        var outcome = await new RecordEnsDeclarationHandler(
                ARepository(RecordReferenceResult.VehicleNotOnConvoy), store, Substitute.For<IDeclarationSnapshots>())
            .HandleAsync(ACommand(), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordEnsOutcome.VehicleNotOnConvoy);
        await store.DidNotReceive().SaveAsync(Arg.Any<EnsDeclarationReadModel>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A reference is write-once: the declaration row settles the race, not a read-then-write here.
    /// </summary>
    [Fact]
    public async Task Refuses_a_second_declaration_for_the_same_vehicle()
    {
        var store = AnEmptyStore();

        var outcome = await new RecordEnsDeclarationHandler(
                ARepository(RecordReferenceResult.AlreadyRecorded), store, Substitute.For<IDeclarationSnapshots>())
            .HandleAsync(ACommand(), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordEnsOutcome.AlreadyRecorded);
        await store.DidNotReceive().SaveAsync(Arg.Any<EnsDeclarationReadModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_already_recorded_when_storage_refuses_a_second_write_of_the_detail()
    {
        var store = Substitute.For<IEnsDeclarationStore>();
        store.SaveAsync(Arg.Any<EnsDeclarationReadModel>(), Arg.Any<CancellationToken>()).Returns(false);

        var outcome = await new RecordEnsDeclarationHandler(ARepository(), store, Substitute.For<IDeclarationSnapshots>()).HandleAsync(
            ACommand(), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordEnsOutcome.AlreadyRecorded);
    }

    /// <summary>
    /// Several ENS fields are non-amendable, so a mistake is corrected by invalidating the declaration in
    /// ICS2 and filing a new one. Superseding withdraws it and keeps its MRN as history.
    /// </summary>
    [Fact]
    public async Task Supersedes_a_declaration_so_a_refiled_one_can_be_recorded()
    {
        var repository = Substitute.For<IDeclarationRepository>();
        repository.WithdrawAsync(42, Vin, DeclarationKind.Ens, null, Arg.Any<CancellationToken>()).Returns(true);

        var outcome = await new SupersedeEnsDeclarationHandler(repository).HandleAsync(
            new SupersedeEnsDeclarationCommand(42, Vin), TestContext.Current.CancellationToken);

        outcome.Should().Be(SupersedeEnsOutcome.Superseded);
    }

    [Fact]
    public async Task Reports_nothing_to_supersede_when_no_declaration_was_recorded()
    {
        var repository = Substitute.For<IDeclarationRepository>();
        repository.WithdrawAsync(42, Vin, DeclarationKind.Ens, null, Arg.Any<CancellationToken>()).Returns(false);

        var outcome = await new SupersedeEnsDeclarationHandler(repository).HandleAsync(
            new SupersedeEnsDeclarationCommand(42, Vin), TestContext.Current.CancellationToken);

        outcome.Should().Be(SupersedeEnsOutcome.NotRecorded);
    }

    [Fact]
    public async Task Reads_back_the_detail_of_the_current_declaration()
    {
        var detail = new EnsDeclarationReadModel(7, Mrn, Accepted, "groundofficer", null);
        var store = Substitute.For<IEnsDeclarationStore>();
        store.GetAsync(7, Arg.Any<CancellationToken>()).Returns(detail);

        var read = await new GetEnsDeclarationHandler(ARepository(), store).HandleAsync(
            new GetEnsDeclarationQuery(42, Vin), TestContext.Current.CancellationToken);

        read.Should().Be(detail);
    }

    [Fact]
    public async Task Reads_nothing_when_no_declaration_exists()
    {
        var repository = Substitute.For<IDeclarationRepository>();

        var read = await new GetEnsDeclarationHandler(repository, AnEmptyStore()).HandleAsync(
            new GetEnsDeclarationQuery(42, Vin), TestContext.Current.CancellationToken);

        read.Should().BeNull();
    }

    /// <summary>
    /// A declaration references a consignment; it does not describe one. The record is serialised into
    /// durable storage, so a field added here would persist.
    /// </summary>
    [Fact]
    public void The_recorded_declaration_has_nowhere_to_put_a_delivery_detail()
    {
        typeof(EnsDeclarationReadModel).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo("DeclarationId", "Mrn", "AcceptedAt", "FiledBy", "FilingReference");
    }
}
