using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>
/// How a box's journey ends (ADR 0015), pinned edge by edge. A box with no outcome yet is still
/// travelling; <c>Accepted</c>, <c>Seized</c> and <c>Undeliverable</c> are terminal, and a
/// returned box starts afresh on a later convoy rather than moving on from here.
/// </summary>
public class BoxOutcomesTests
{
    public static TheoryData<BoxOutcome?, BoxOutcome> LegalEdges => new()
    {
        { null, BoxOutcome.Delivered },
        { null, BoxOutcome.Seized },
        { null, BoxOutcome.ReturnedToHub },
        { null, BoxOutcome.Undeliverable },
        { BoxOutcome.Delivered, BoxOutcome.Accepted },
        { BoxOutcome.Delivered, BoxOutcome.Seized },
        { BoxOutcome.Delivered, BoxOutcome.ReturnedToHub },
        { BoxOutcome.Delivered, BoxOutcome.Undeliverable },
    };

    public static TheoryData<BoxOutcome?, BoxOutcome> IllegalEdges => new()
    {
        { null, BoxOutcome.Accepted },
        { BoxOutcome.Delivered, BoxOutcome.Delivered },
        { BoxOutcome.Accepted, BoxOutcome.Delivered },
        { BoxOutcome.Accepted, BoxOutcome.Seized },
        { BoxOutcome.Accepted, BoxOutcome.Undeliverable },
        { BoxOutcome.Seized, BoxOutcome.Delivered },
        { BoxOutcome.Seized, BoxOutcome.ReturnedToHub },
        { BoxOutcome.Undeliverable, BoxOutcome.Delivered },
        { BoxOutcome.Undeliverable, BoxOutcome.Accepted },
        { BoxOutcome.ReturnedToHub, BoxOutcome.Delivered },
        { BoxOutcome.ReturnedToHub, BoxOutcome.Seized },
    };

    [Theory]
    [MemberData(nameof(LegalEdges))]
    public void A_legal_edge_is_allowed(BoxOutcome? from, BoxOutcome to) =>
        BoxOutcomes.CanRecord(from, to).Should().BeTrue();

    [Theory]
    [MemberData(nameof(IllegalEdges))]
    public void An_illegal_edge_is_refused(BoxOutcome? from, BoxOutcome to) =>
        BoxOutcomes.CanRecord(from, to).Should().BeFalse();

    [Theory]
    [InlineData("damaged")]
    [InlineData("stolen")]
    [InlineData("other")]
    public void An_undeliverable_box_needs_one_of_the_bounded_reasons(string reason) =>
        BoxOutcomes.Check(BoxOutcome.Undeliverable, reason, hubIsRegistered: false)
            .Should().Be(BoxOutcomeCheck.Ok);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("the driver dropped it at 12 Some Street")]
    public void An_undeliverable_box_without_a_bounded_reason_is_refused(string? reason) =>
        BoxOutcomes.Check(BoxOutcome.Undeliverable, reason, hubIsRegistered: false)
            .Should().Be(BoxOutcomeCheck.ReasonRequired);

    [Fact]
    public void A_box_returned_to_an_unregistered_hub_is_refused() =>
        BoxOutcomes.Check(BoxOutcome.ReturnedToHub, null, hubIsRegistered: false)
            .Should().Be(BoxOutcomeCheck.HubNotRegistered);

    [Fact]
    public void A_box_returned_to_a_registered_hub_is_allowed() =>
        BoxOutcomes.Check(BoxOutcome.ReturnedToHub, null, hubIsRegistered: true)
            .Should().Be(BoxOutcomeCheck.Ok);

    [Theory]
    [InlineData(BoxOutcome.Delivered, true)]
    [InlineData(BoxOutcome.Accepted, true)]
    [InlineData(BoxOutcome.Seized, false)]
    [InlineData(BoxOutcome.ReturnedToHub, false)]
    [InlineData(BoxOutcome.Undeliverable, false)]
    public void Only_delivered_and_accepted_boxes_count_as_delivered(BoxOutcome outcome, bool counts) =>
        BoxOutcomes.CountsAsDelivered(outcome).Should().Be(counts);

    [Fact]
    public void Delivered_value_sums_only_the_boxes_that_were_delivered()
    {
        var value = BoxOutcomes.DeliveredValue(
        [
            (BoxOutcome.Delivered, 100m),
            (BoxOutcome.Accepted, 50m),
            (BoxOutcome.Seized, 400m),
            (BoxOutcome.Undeliverable, 30m),
            (BoxOutcome.ReturnedToHub, 20m),
            (null, 7m),
        ]);

        value.Should().Be(150m);
    }
}
