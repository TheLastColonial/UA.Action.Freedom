using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>
/// The staleness table of docs/domain/customs-declarations.md, row by row: a declaration is stale when
/// the load it was written from differs from the load now (ADR 0005).
/// </summary>
public class StalenessTests
{
    private const string Vin = "WVWZZZ1JZXW000001";

    private static readonly Guid Receiver = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static LoadItem AnItem(
        int categoryId = 3, int? quantity = 10, decimal? valueGbp = 25m, string? commodityCode = null,
        Guid? id = null) =>
        new(id ?? Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), categoryId, quantity, valueGbp, commodityCode);

    private static LoadBox ABox(
        int boxId = 1, int weightKg = 20, Guid? receiver = null, bool registered = true, params LoadItem[] items) =>
        new(boxId, weightKg, receiver ?? Receiver, registered, items.Length == 0 ? [AnItem()] : items);

    private static LoadSnapshot ALoad(
        bool withdrawn = false, string vin = Vin, int version = LoadSnapshot.CurrentVersion, params LoadBox[] boxes) =>
        new(version, vin, withdrawn, boxes.Length == 0 ? [ABox()] : boxes);

    [Fact]
    public void An_unchanged_load_is_not_stale() =>
        Staleness.IsStale(ALoad(), ALoad()).Should().BeFalse();

    [Fact]
    public void A_box_added_makes_it_stale() =>
        Staleness.IsStale(ALoad(boxes: ABox(1)), ALoad(boxes: [ABox(1), ABox(2)])).Should().BeTrue();

    [Fact]
    public void A_box_removed_or_moved_to_another_vehicle_makes_it_stale() =>
        Staleness.IsStale(ALoad(boxes: [ABox(1), ABox(2)]), ALoad(boxes: ABox(1))).Should().BeTrue();

    [Fact]
    public void A_box_replaced_by_another_makes_it_stale() =>
        Staleness.IsStale(ALoad(boxes: ABox(1)), ALoad(boxes: ABox(7))).Should().BeTrue();

    [Fact]
    public void The_order_boxes_are_listed_in_does_not_matter() =>
        Staleness.IsStale(ALoad(boxes: [ABox(1), ABox(2)]), ALoad(boxes: [ABox(2), ABox(1)])).Should().BeFalse();

    [Fact]
    public void A_box_weight_change_makes_it_stale() =>
        Staleness.IsStale(ALoad(boxes: ABox(weightKg: 20)), ALoad(boxes: ABox(weightKg: 21))).Should().BeTrue();

    [Fact]
    public void A_box_changing_receiver_makes_it_stale() =>
        Staleness.IsStale(
            ALoad(boxes: ABox()),
            ALoad(boxes: ABox(receiver: Guid.Parse("22222222-2222-2222-2222-222222222222")))).Should().BeTrue();

    [Fact]
    public void An_item_added_or_removed_makes_it_stale() =>
        Staleness.IsStale(
            ALoad(boxes: ABox(items: AnItem())),
            ALoad(boxes: ABox(items: [AnItem(), AnItem(id: Guid.NewGuid())]))).Should().BeTrue();

    [Fact]
    public void An_item_changing_category_makes_it_stale() =>
        Staleness.IsStale(
            ALoad(boxes: ABox(items: AnItem(categoryId: 3))),
            ALoad(boxes: ABox(items: AnItem(categoryId: 4)))).Should().BeTrue();

    [Fact]
    public void An_item_changing_quantity_makes_it_stale() =>
        Staleness.IsStale(
            ALoad(boxes: ABox(items: AnItem(quantity: 10))),
            ALoad(boxes: ABox(items: AnItem(quantity: 12)))).Should().BeTrue();

    [Fact]
    public void An_item_changing_value_makes_it_stale() =>
        Staleness.IsStale(
            ALoad(boxes: ABox(items: AnItem(valueGbp: 25m))),
            ALoad(boxes: ABox(items: AnItem(valueGbp: 30m)))).Should().BeTrue();

    [Fact]
    public void An_item_changing_commodity_code_makes_it_stale() =>
        Staleness.IsStale(
            ALoad(boxes: ABox(items: AnItem(commodityCode: null))),
            ALoad(boxes: ABox(items: AnItem(commodityCode: "9919000000")))).Should().BeTrue();

    [Fact]
    public void The_vehicle_being_swapped_makes_it_stale() =>
        Staleness.IsStale(ALoad(), ALoad(vin: "WVWZZZ1JZXW000002")).Should().BeTrue();

    [Fact]
    public void The_vehicle_being_withdrawn_makes_it_stale() =>
        Staleness.IsStale(ALoad(withdrawn: false), ALoad(withdrawn: true)).Should().BeTrue();

    [Fact]
    public void A_receiver_that_stops_being_registered_makes_it_stale() =>
        Staleness.IsStale(ALoad(boxes: ABox(registered: true)), ALoad(boxes: ABox(registered: false))).Should().BeTrue();

    [Fact]
    public void A_receiver_that_was_never_registered_is_not_a_change() =>
        Staleness.IsStale(ALoad(boxes: ABox(registered: false)), ALoad(boxes: ABox(registered: false))).Should().BeFalse();

    [Fact]
    public void A_receiver_registered_after_the_snapshot_is_not_a_change() =>
        Staleness.IsStale(ALoad(boxes: ABox(registered: false)), ALoad(boxes: ABox(registered: true))).Should().BeFalse();

    [Fact]
    public void A_snapshot_of_a_version_this_code_does_not_know_still_compares_on_the_fields_it_has()
    {
        var older = ALoad(version: 1);
        var newer = ALoad(version: LoadSnapshot.CurrentVersion);

        Staleness.IsStale(older, newer).Should().BeFalse();
    }
}
