using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>
/// A policy names the drivers it covers. Removing a driver leaves the rest covered; a driver added
/// afterwards is uncovered until the Dispatcher records the policy again.
/// </summary>
public class VehicleInsuranceTests
{
    private static readonly Guid Olena = Guid.NewGuid();
    private static readonly Guid Taras = Guid.NewGuid();

    [Fact]
    public void Every_driver_named_on_the_policy_is_covered()
    {
        VehicleInsurance.CoversAllDrivers(drivers: [Olena, Taras], covered: [Taras, Olena]).Should().BeTrue();
    }

    [Fact]
    public void A_driver_added_after_the_policy_was_recorded_is_not_covered()
    {
        VehicleInsurance.CoversAllDrivers(drivers: [Olena, Taras], covered: [Olena]).Should().BeFalse();
    }

    [Fact]
    public void Removing_a_driver_leaves_the_others_covered()
    {
        VehicleInsurance.CoversAllDrivers(drivers: [Olena], covered: [Olena, Taras]).Should().BeTrue();
    }

    [Fact]
    public void A_vehicle_with_no_drivers_has_nobody_left_uncovered()
    {
        VehicleInsurance.CoversAllDrivers(drivers: [], covered: []).Should().BeTrue();
    }
}
