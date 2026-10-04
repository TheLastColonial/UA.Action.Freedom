using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>What a line of vehicle equipment costs the convoy (O13).</summary>
public class VehicleEquipmentTests
{
    [Fact]
    public void A_recorded_cost_wins()
    {
        VehicleEquipment.CountedCostGbp(quantity: 3, costGbp: 20m, unitCostGbp: 9m).Should().Be(20m);
    }

    [Fact]
    public void Without_a_recorded_cost_it_is_the_quantity_at_the_unit_cost()
    {
        VehicleEquipment.CountedCostGbp(quantity: 3, costGbp: null, unitCostGbp: 6.50m).Should().Be(19.50m);
    }

    [Fact]
    public void With_neither_it_costs_nothing()
    {
        VehicleEquipment.CountedCostGbp(quantity: 3, costGbp: null, unitCostGbp: null).Should().Be(0m);
    }
}
