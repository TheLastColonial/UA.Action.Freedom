using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>
/// Cargo is a box allocated to a truck-list entry. A box is on at most one entry, so putting it on
/// a second moves it.
/// </summary>
public class BoxAllocationTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 9, 0, 0, DateTimeKind.Utc);

    private static ConvoyVehicle AnEntry(string vin = "WVWZZZ1JZXW000001", DateTime? withdrawnAt = null) => new()
    {
        ConvoyId = new ConvoyId(7),
        Vin = vin,
        WithdrawnAt = withdrawnAt,
    };

    private static BoxAllocation AnAllocation(int boxId, string vin = "WVWZZZ1JZXW000001") =>
        new(new ConvoyId(7), vin, boxId, Now.AddDays(-1));

    [Fact]
    public void A_box_allocated_to_a_vehicle_is_listed_against_it()
    {
        var result = BoxAllocations.Allocate([], AnEntry(), boxId: 3, Now);

        result.Outcome.Should().Be(AllocateOutcome.Allocated);
        result.Allocations.Should().ContainSingle()
            .Which.Should().Be(new BoxAllocation(new ConvoyId(7), "WVWZZZ1JZXW000001", 3, Now));
    }

    [Fact]
    public void Allocating_a_box_to_a_second_vehicle_moves_it()
    {
        var onA = AnAllocation(3, vin: "WVWZZZ1JZXW000001");

        var result = BoxAllocations.Allocate([onA], AnEntry(vin: "WVWZZZ1JZXW000002"), boxId: 3, Now);

        result.Outcome.Should().Be(AllocateOutcome.Moved);
        result.Allocations.Should().ContainSingle()
            .Which.Vin.Should().Be("WVWZZZ1JZXW000002");
    }

    [Fact]
    public void Allocating_a_box_already_on_the_vehicle_changes_nothing()
    {
        var onA = AnAllocation(3);

        var result = BoxAllocations.Allocate([onA], AnEntry(), boxId: 3, Now);

        result.Outcome.Should().Be(AllocateOutcome.AlreadyAllocated);
        result.Allocations.Should().Equal(onA);
    }

    [Fact]
    public void A_withdrawn_vehicle_takes_no_new_cargo()
    {
        var withdrawn = AnEntry(withdrawnAt: Now.AddHours(-1));

        var result = BoxAllocations.Allocate([], withdrawn, boxId: 3, Now);

        result.Outcome.Should().Be(AllocateOutcome.VehicleWithdrawn);
        result.Allocations.Should().BeEmpty();
    }

    [Fact]
    public void Removing_a_box_takes_it_off_its_vehicle_and_leaves_the_others()
    {
        var result = BoxAllocations.Remove([AnAllocation(3), AnAllocation(4)], AnEntry(), boxId: 3);

        result.Outcome.Should().Be(RemoveAllocationOutcome.Removed);
        result.Allocations.Select(a => a.BoxId).Should().Equal(4);
    }

    [Fact]
    public void Removing_a_box_that_is_on_another_vehicle_is_a_caller_mistake()
    {
        var onB = AnAllocation(3, vin: "WVWZZZ1JZXW000002");

        var result = BoxAllocations.Remove([onB], AnEntry(), boxId: 3);

        result.Outcome.Should().Be(RemoveAllocationOutcome.NotAllocated);
        result.Allocations.Should().Equal(onB);
    }
}
