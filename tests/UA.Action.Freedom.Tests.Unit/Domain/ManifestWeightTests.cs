using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>
/// The total weight a border check is given for a manifest.
/// </summary>
/// <remarks>
/// The fixed 200 kg (two drivers and their bags) and 45 kg (fuel) are a deliberate border-check
/// estimate, not a bug — docs/domain/key-concepts.md says so explicitly. These tests exist so that
/// anyone who reads the padding as an error and "corrects" it has to argue with a test first.
/// </remarks>
public class ManifestWeightTests
{
    private const int DriversAndBagsKg = 200;
    private const int FuelKg = 45;

    private static Vehicle AVehicle(int weightKg) => new()
    {
        VIN = "WVWZZZ1JZXW000001",
        Plate = "AB12CDE",
        WeightKg = weightKg,
    };

    private static Box ABox(int weightKg, int id) => new()
    {
        Id = new BoxId(id),
        WeightKg = weightKg,
    };

    private static Manifest AManifest(Vehicle? vehicle, params Box[] boxes) => new()
    {
        Id = new ManifestId("MAN-0001"),
        Vehicle = vehicle,
        Boxes = boxes,
    };

    [Fact]
    public void Adds_the_kerb_weight_the_cargo_and_the_fixed_allowances()
    {
        var manifest = AManifest(AVehicle(1_400), ABox(30, 1), ABox(12, 2));

        manifest.TotalWeightKg().Should().Be(1_400 + 42 + DriversAndBagsKg + FuelKg);
    }

    [Fact]
    public void Still_reports_the_crew_and_fuel_allowance_for_an_empty_vehicle()
    {
        var manifest = AManifest(AVehicle(1_400));

        manifest.TotalWeightKg().Should().Be(1_400 + DriversAndBagsKg + FuelKg);
    }

    [Fact]
    public void Reports_only_the_cargo_and_allowances_before_a_vehicle_is_assigned()
    {
        // A manifest is Created before it is fully populated, so the weight has to be readable
        // while the vehicle is still null rather than throwing at the point someone opens it.
        var manifest = AManifest(vehicle: null, ABox(30, 1));

        manifest.TotalWeightKg().Should().Be(30 + DriversAndBagsKg + FuelKg);
    }
}
