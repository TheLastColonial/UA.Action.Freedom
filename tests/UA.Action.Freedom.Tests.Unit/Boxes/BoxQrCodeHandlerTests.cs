using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Boxes;

namespace UA.Action.Freedom.Tests.Unit.Boxes;

/// <summary>
/// QR labels for boxes: issuing, re-issuing, revoking, and resolving a scanned token.
/// </summary>
/// <remarks>
/// A label ties the cardboard to its record. It is not box contents, so issuing one is allowed
/// whatever state the box is in — unlike packing an item, which a validated box refuses. Re-
/// issuing revokes the previous code so a lost label stops working; the handler leaves the
/// revoke-and-replace to the repository, which does it as one act (docs/domain/key-concepts.md
/// § Box, § Data Sensitivity).
/// </remarks>
public class BoxQrCodeHandlerTests
{
    private const int BoxId = 7;

    private static readonly Guid Token = new("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    private static BoxReadModel ABox(bool validated = false) => new(
        BoxId,
        WeightKg: validated ? 24 : 0,
        ReceiverRef: null,
        House: "Unit 4",
        Street: "Cross Road",
        City: "Coventry",
        Country: "United Kingdom",
        Postcode: "CV1 2AB",
        ValidatedByPersonId: validated ? Guid.NewGuid() : null,
        ValidatedAt: validated ? new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc) : null);

    private static BoxQrCodeReadModel ACode(Guid? token = null) => new(
        token ?? Token, BoxId, new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc), RevokedAt: null);

    [Fact]
    public async Task Issuing_a_label_for_a_box_that_exists_mints_a_token()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.ExistsAsync(BoxId, Arg.Any<CancellationToken>()).Returns(true);
        repository.IssueQrCodeAsync(BoxId, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => ACode(call.ArgAt<Guid>(1)));
        var handler = new IssueBoxQrCodeHandler(repository);

        var code = await handler.HandleAsync(new IssueBoxQrCodeCommand(BoxId), CancellationToken.None);

        code.Should().NotBeNull();
        await repository.Received(1).IssueQrCodeAsync(
            BoxId, Arg.Is<Guid>(token => token != Guid.Empty), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Issuing_a_label_for_a_box_that_does_not_exist_mints_nothing()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.ExistsAsync(BoxId, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new IssueBoxQrCodeHandler(repository);

        var code = await handler.HandleAsync(new IssueBoxQrCodeCommand(BoxId), CancellationToken.None);

        code.Should().BeNull();
        await repository.DidNotReceive().IssueQrCodeAsync(
            Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Re_issuing_is_a_single_call_the_repository_settles()
    {
        // The old label must stop resolving at the instant the new one starts. That is the
        // repository's transaction, not a revoke-then-issue the handler orchestrates.
        var repository = Substitute.For<IBoxRepository>();
        repository.ExistsAsync(BoxId, Arg.Any<CancellationToken>()).Returns(true);
        repository.IssueQrCodeAsync(BoxId, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => ACode(call.ArgAt<Guid>(1)));
        var handler = new IssueBoxQrCodeHandler(repository);

        await handler.HandleAsync(new IssueBoxQrCodeCommand(BoxId), CancellationToken.None);

        await repository.Received(1).IssueQrCodeAsync(
            Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().RevokeActiveQrCodeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_validated_box_can_still_be_issued_a_replacement_label()
    {
        // The freeze protects the confirmed weight and contents. A label is neither.
        var repository = Substitute.For<IBoxRepository>();
        repository.ExistsAsync(BoxId, Arg.Any<CancellationToken>()).Returns(true);
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox(validated: true));
        repository.IssueQrCodeAsync(BoxId, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => ACode(call.ArgAt<Guid>(1)));
        var handler = new IssueBoxQrCodeHandler(repository);

        var code = await handler.HandleAsync(new IssueBoxQrCodeCommand(BoxId), CancellationToken.None);

        code.Should().NotBeNull();
    }

    [Fact]
    public async Task Revoking_when_there_is_no_active_label_reports_not_found()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.RevokeActiveQrCodeAsync(BoxId, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new RevokeBoxQrCodeHandler(repository);

        var outcome = await handler.HandleAsync(new RevokeBoxQrCodeCommand(BoxId), CancellationToken.None);

        outcome.Should().Be(RevokeBoxQrCodeOutcome.NotFound);
    }

    [Fact]
    public async Task Revoking_an_active_label_reports_it_revoked()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.RevokeActiveQrCodeAsync(BoxId, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new RevokeBoxQrCodeHandler(repository);

        var outcome = await handler.HandleAsync(new RevokeBoxQrCodeCommand(BoxId), CancellationToken.None);

        outcome.Should().Be(RevokeBoxQrCodeOutcome.Revoked);
    }

    [Fact]
    public async Task Resolving_an_unknown_or_revoked_token_yields_nothing()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.ResolveActiveQrCodeAsync(Token, Arg.Any<CancellationToken>()).Returns((BoxQrCodeReadModel?)null);
        var handler = new ResolveBoxByQrCodeHandler(repository);

        var box = await handler.HandleAsync(new ResolveBoxByQrCodeQuery(Token), CancellationToken.None);

        box.Should().BeNull();
        await repository.DidNotReceive().GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolving_an_active_token_returns_the_box_it_identifies()
    {
        var repository = Substitute.For<IBoxRepository>();
        repository.ResolveActiveQrCodeAsync(Token, Arg.Any<CancellationToken>()).Returns(ACode());
        repository.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ABox());
        var handler = new ResolveBoxByQrCodeHandler(repository);

        var box = await handler.HandleAsync(new ResolveBoxByQrCodeQuery(Token), CancellationToken.None);

        box.Should().NotBeNull();
        box!.Id.Should().Be(BoxId);
    }
}
