using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Boxes;

/// <summary>
/// An attested box is never edited: if its contents must change it is voided and replaced (ADR 0011).
/// </summary>
public class ReplaceBoxHandlerTests
{
    private const int BoxId = 7;

    private const int ReplacementId = 8;

    private static readonly Guid Loader = new("2b9c1e40-7d8a-4c31-9f52-6a0b8d3e5c11");

    private static BoxReadModel ABox(bool validated = true, bool voided = false) => new(
        BoxId,
        WeightKg: validated ? 24 : 0,
        WidthCm: null,
        DepthCm: null,
        HeightCm: null,
        ReceiverRef: null,
        LocationId: 3,
        ValidatedByPersonId: validated ? Loader : null,
        ValidatedAt: validated ? new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc) : null,
        VoidedAt: voided ? new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Utc) : null);

    private sealed record Fixture(ReplaceBoxHandler Handler, IBoxRepository Boxes);

    private static Fixture AFixture(BoxReadModel? box)
    {
        var boxes = Substitute.For<IBoxRepository>();
        boxes.GetByIdAsync(BoxId, Arg.Any<CancellationToken>()).Returns(box);
        boxes.ReplaceAsync(BoxId, Arg.Any<CancellationToken>()).Returns(ReplacementId);

        return new Fixture(new ReplaceBoxHandler(boxes), boxes);
    }

    [Fact]
    public async Task An_attested_box_is_replaced_by_a_new_box()
    {
        var fixture = AFixture(ABox());

        var result = await fixture.Handler.HandleAsync(new ReplaceBoxCommand(BoxId), CancellationToken.None);

        result.Outcome.Should().Be(ReplaceBoxOutcome.Replaced);
        result.ReplacementBoxId.Should().Be(ReplacementId);
    }

    [Fact]
    public async Task A_box_nobody_has_attested_is_edited_not_replaced()
    {
        var fixture = AFixture(ABox(validated: false));

        var result = await fixture.Handler.HandleAsync(new ReplaceBoxCommand(BoxId), CancellationToken.None);

        result.Outcome.Should().Be(ReplaceBoxOutcome.NotAttested);
        await fixture.Boxes.DidNotReceive().ReplaceAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_box_that_does_not_exist_cannot_be_replaced()
    {
        var fixture = AFixture(box: null);

        var result = await fixture.Handler.HandleAsync(new ReplaceBoxCommand(BoxId), CancellationToken.None);

        result.Outcome.Should().Be(ReplaceBoxOutcome.NotFound);
    }

    [Fact]
    public async Task A_box_that_was_already_voided_cannot_be_replaced_again()
    {
        var fixture = AFixture(ABox(voided: true));

        var result = await fixture.Handler.HandleAsync(new ReplaceBoxCommand(BoxId), CancellationToken.None);

        result.Outcome.Should().Be(ReplaceBoxOutcome.AlreadyVoided);
        await fixture.Boxes.DidNotReceive().ReplaceAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_replacement_that_loses_a_race_is_reported_by_re_reading_the_box()
    {
        // Two Loaders pressing Replace at once: the database settles it, only one replacement is made.
        var fixture = AFixture(ABox());
        fixture.Boxes.ReplaceAsync(BoxId, Arg.Any<CancellationToken>()).Returns((int?)null);
        fixture.Boxes.GetByIdAsync(BoxId, Arg.Any<CancellationToken>())
            .Returns(ABox(), ABox(voided: true));

        var result = await fixture.Handler.HandleAsync(new ReplaceBoxCommand(BoxId), CancellationToken.None);

        result.Outcome.Should().Be(ReplaceBoxOutcome.AlreadyVoided);
    }
}
