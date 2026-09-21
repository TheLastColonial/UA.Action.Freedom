using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>
/// The truck-list entry: one vehicle, on one convoy.
/// </summary>
/// <remarks>
/// This is the row the consolidation exists for. "This vehicle is travelling with this convoy" used
/// to be written in four places — <c>dbo.Vehicle.ConvoyId</c>, the manifest's two loose foreign
/// keys, the crew table and the insurance table — and reconciled in none of them. It now has one
/// home, and the crew, the insurance and the manifest all hang off it.
///
/// Withdrawal is a stamp rather than a delete because a vehicle that breaks down on the road leaves
/// the convoy but keeps its paperwork: its manifest and its GMR describe a load that is still real.
/// </remarks>
public class ConvoyVehicleTests
{
    private static ConvoyVehicle AnEntry(DateTime? withdrawnAt = null, string? reason = null) => new()
    {
        ConvoyId = new ConvoyId(7),
        Vin = "WVWZZZ1JZXW000001",
        AddedAt = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc),
        WithdrawnAt = withdrawnAt,
        WithdrawnReason = reason,
    };

    [Fact]
    public void A_vehicle_on_the_list_is_travelling_with_the_convoy()
    {
        AnEntry().Travelling.Should().BeTrue();
        AnEntry().Withdrawn.Should().BeFalse();
    }

    [Fact]
    public void A_withdrawn_vehicle_is_no_longer_travelling_but_stays_on_the_list()
    {
        var brokenDown = AnEntry(
            withdrawnAt: new DateTime(2026, 3, 4, 14, 30, 0, DateTimeKind.Utc),
            reason: "Gearbox failure near Poznan");

        brokenDown.Travelling.Should().BeFalse();
        brokenDown.Withdrawn.Should().BeTrue();

        // The row is the record of what happened, so it keeps naming the convoy it left.
        brokenDown.ConvoyId.Should().Be(new ConvoyId(7));
        brokenDown.WithdrawnReason.Should().Be("Gearbox failure near Poznan");
    }

    [Fact]
    public void Travelling_is_one_statement_shared_with_the_read_side()
    {
        // Mirrors VehicleInsurance.InCover: the rule is a static so the flat read model can apply
        // exactly the same test to a row it read, rather than re-implementing it in SQL.
        ConvoyVehicle.IsTravelling(withdrawnAt: null).Should().BeTrue();
        ConvoyVehicle.IsTravelling(withdrawnAt: DateTime.UtcNow).Should().BeFalse();
    }
}
