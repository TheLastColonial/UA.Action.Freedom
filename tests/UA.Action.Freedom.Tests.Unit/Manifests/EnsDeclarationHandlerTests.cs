using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Manifests;

/// <summary>
/// Recording the ICS2 Entry Summary Declaration a manifest's crossing was accepted under.
/// </summary>
/// <remarks>
/// Freedom does not submit the ENS — ICS2's Shared Trader Interface speaks eDelivery AS4, and an
/// always-on inbound access point is what <c>docs/recommendations.md</c> §4.1 declines. It records
/// the MRN a Ground Officer obtained, because that reference is what the ELO envelope and the GMR
/// both need, and until it exists neither is real (<c>docs/adr/0003</c>).
///
/// <para>
/// The MRN is write-once by the same reasoning as <c>Manifest.GmrSubmittedAt</c>: the envelope names
/// it, so silently replacing one would leave French customs pairing a crossing against a formality
/// Freedom no longer believes in. Correcting it is an explicit supersede, not an overwrite.
/// </para>
/// </remarks>
public class EnsDeclarationHandlerTests
{
    private const string Id = "MAN-0001";

    private const string Mrn = "25FR17551780961AT5";

    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly DateTime Stamped = new(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

    private static readonly DateTimeOffset Accepted = new(2026, 8, 24, 9, 30, 0, TimeSpan.Zero);

    private static ManifestReadModel AManifest(bool frozen = false) => new(
        Id, 42, Vin, ManifestStatus.Proposed, null, FerryBookingComplete: false,
        GmrSubmittedAt: frozen ? Stamped : null);

    private static RecordEnsDeclarationCommand ACommand(string mrn = Mrn) =>
        new(Id, mrn, Accepted, FiledBy: "groundofficer", FilingReference: "STP-2026-0001");

    private static IManifestRepository ARepositoryHolding(ManifestReadModel? manifest)
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(manifest);
        return repository;
    }

    /// <summary>A store that accepts the first declaration for a manifest.</summary>
    private static IEnsDeclarationStore AnEmptyStore()
    {
        var store = Substitute.For<IEnsDeclarationStore>();
        store.SaveAsync(Arg.Any<EnsDeclarationReadModel>(), Arg.Any<CancellationToken>()).Returns(true);
        return store;
    }

    [Fact]
    public async Task Records_the_declaration_against_the_manifest()
    {
        var store = AnEmptyStore();
        var handler = new RecordEnsDeclarationHandler(ARepositoryHolding(AManifest()), store);

        var outcome = await handler.HandleAsync(ACommand(), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordEnsOutcome.Recorded);
        await store.Received(1).SaveAsync(
            Arg.Is<EnsDeclarationReadModel>(declaration =>
                declaration.ManifestId == Id
                && declaration.Mrn == Mrn
                && declaration.AcceptedAt == Accepted
                && declaration.FiledBy == "groundofficer"
                && declaration.FilingReference == "STP-2026-0001"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A malformed MRN is refused here rather than at the border. French customs answers an envelope
    /// naming an unrecognised formality with FONC-ERR-004, by which point the manifest is frozen and
    /// the convoy is loading.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("25fr17551780961at5")]
    public async Task Refuses_an_mrn_that_is_not_shaped_like_one(string mrn)
    {
        var store = AnEmptyStore();
        var handler = new RecordEnsDeclarationHandler(ARepositoryHolding(AManifest()), store);

        var outcome = await handler.HandleAsync(ACommand(mrn), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordEnsOutcome.MalformedMrn);
        await store.DidNotReceive().SaveAsync(
            Arg.Any<EnsDeclarationReadModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_not_found_for_a_manifest_that_does_not_exist()
    {
        var store = AnEmptyStore();
        var handler = new RecordEnsDeclarationHandler(ARepositoryHolding(null), store);

        var outcome = await handler.HandleAsync(ACommand(), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordEnsOutcome.ManifestNotFound);
        await store.DidNotReceive().SaveAsync(
            Arg.Any<EnsDeclarationReadModel>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The store settles the race, not a read-then-write here: two dispatchers recording different
    /// MRNs at once must resolve to one declaration, and only storage can see both.
    /// </summary>
    [Fact]
    public async Task Refuses_a_second_declaration_for_the_same_manifest()
    {
        var store = Substitute.For<IEnsDeclarationStore>();
        store.SaveAsync(Arg.Any<EnsDeclarationReadModel>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = new RecordEnsDeclarationHandler(ARepositoryHolding(AManifest()), store);

        var outcome = await handler.HandleAsync(ACommand(), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordEnsOutcome.AlreadyRecorded);
    }

    /// <summary>
    /// Once the manifest is frozen its envelope has already been asked for, naming whatever
    /// formalities existed then. Recording an MRN afterwards would produce a reference nothing
    /// reads, which is worse than refusing and saying so.
    /// </summary>
    [Fact]
    public async Task Refuses_to_record_against_a_frozen_manifest()
    {
        var store = AnEmptyStore();
        var handler = new RecordEnsDeclarationHandler(ARepositoryHolding(AManifest(frozen: true)), store);

        var outcome = await handler.HandleAsync(ACommand(), TestContext.Current.CancellationToken);

        outcome.Should().Be(RecordEnsOutcome.ManifestFrozen);
        await store.DidNotReceive().SaveAsync(
            Arg.Any<EnsDeclarationReadModel>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Several ENS fields are non-amendable — mode of transport, declarant, office of first entry,
    /// the carrier identifier — so a mistake in one is corrected by invalidating the declaration in
    /// ICS2 and filing a new one. Freedom has to be able to follow that, which is why replacing an
    /// MRN is an explicit act with its own route rather than a second PUT.
    /// </summary>
    [Fact]
    public async Task Supersedes_a_declaration_so_a_refiled_one_can_be_recorded()
    {
        var store = Substitute.For<IEnsDeclarationStore>();
        store.SupersedeAsync(Id, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new SupersedeEnsDeclarationHandler(ARepositoryHolding(AManifest()), store);

        var outcome = await handler.HandleAsync(
            new SupersedeEnsDeclarationCommand(Id), TestContext.Current.CancellationToken);

        outcome.Should().Be(SupersedeEnsOutcome.Superseded);
        await store.Received(1).SupersedeAsync(Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_nothing_to_supersede_when_no_declaration_was_recorded()
    {
        var store = Substitute.For<IEnsDeclarationStore>();
        store.SupersedeAsync(Id, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new SupersedeEnsDeclarationHandler(ARepositoryHolding(AManifest()), store);

        var outcome = await handler.HandleAsync(
            new SupersedeEnsDeclarationCommand(Id), TestContext.Current.CancellationToken);

        outcome.Should().Be(SupersedeEnsOutcome.NotRecorded);
    }

    /// <summary>
    /// The envelope already names the MRN, and a frozen manifest's paperwork is settled. Withdrawing
    /// the declaration under it would leave French customs pairing a crossing against a formality
    /// that no longer exists.
    /// </summary>
    [Fact]
    public async Task Refuses_to_supersede_the_declaration_of_a_frozen_manifest()
    {
        var store = Substitute.For<IEnsDeclarationStore>();
        var handler = new SupersedeEnsDeclarationHandler(ARepositoryHolding(AManifest(frozen: true)), store);

        var outcome = await handler.HandleAsync(
            new SupersedeEnsDeclarationCommand(Id), TestContext.Current.CancellationToken);

        outcome.Should().Be(SupersedeEnsOutcome.ManifestFrozen);
        await store.DidNotReceive().SupersedeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reads_back_the_declaration_recorded_for_a_manifest()
    {
        var declaration = new EnsDeclarationReadModel(Id, Mrn, Accepted, "groundofficer", null);
        var store = Substitute.For<IEnsDeclarationStore>();
        store.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(declaration);

        var read = await new GetManifestEnsHandler(store).HandleAsync(
            new GetManifestEnsQuery(Id), TestContext.Current.CancellationToken);

        read.Should().Be(declaration);
    }

    /// <summary>
    /// A declaration references a consignment; it does not describe one. There is nowhere on the
    /// record to put a receiver, an address or a contact, and this pins that rather than trusting it
    /// — the record is serialised into durable storage, so a field added here would persist.
    /// </summary>
    [Fact]
    public void The_recorded_declaration_has_nowhere_to_put_a_delivery_detail()
    {
        typeof(EnsDeclarationReadModel).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo("ManifestId", "Mrn", "AcceptedAt", "FiledBy", "FilingReference");
    }
}
