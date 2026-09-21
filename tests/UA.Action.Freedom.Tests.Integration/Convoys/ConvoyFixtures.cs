using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Convoys;

/// <summary>
/// Rows the convoy tests arrange directly, and the clean-up that respects the foreign keys.
/// </summary>
/// <remarks>
/// Shared by <see cref="ConvoyRepositoryTests"/> (the journey) and
/// <see cref="ConvoyVehicleRepositoryTests"/> (the truck list, its crew and its insurance), which
/// are split the way the repositories and the tables are.
///
/// <para>
/// <strong>Clean-up order is load-bearing.</strong> <c>FK_Manifest_ConvoyVehicle</c> and
/// <c>FK_ConvoyVehicle_Convoy</c> are both NO ACTION, so manifests go before truck-list rows and
/// truck-list rows before the convoy. Deleting a vehicle cascades its truck-list rows, and those
/// cascade the crew and the insurance — which is why removing a vehicle a manifest still names
/// would be refused, and why the helpers below take the manifests out first.
/// </para>
/// </remarks>
internal static class ConvoyFixtures
{
    internal static readonly DateTime Start = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
    internal static readonly DateTime ExpectedEnd = new(2026, 9, 5, 18, 0, 0, DateTimeKind.Utc);

    /// <summary>The tables both suites need; a database that predates any of them skips the test.</summary>
    internal const string Probe =
        """
        SELECT COUNT(1) FROM dbo.Convoy;
        SELECT COUNT(1) FROM dbo.ConvoyRouteStop;
        SELECT COUNT(1) FROM dbo.ConvoyVehicle;
        SELECT COUNT(1) FROM dbo.ConvoyVehicleCrew;
        SELECT COUNT(1) FROM dbo.ConvoyVehicleInsurance;
        """;

    internal static string NewVin() => "IT" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();

    internal static Task AddVehicleAsync(string vin, InspectionStatus inspection = InspectionStatus.Passed) => ExecuteAsync(
        "INSERT INTO dbo.Vehicle (Vin, Plate, [Year], WeightKg, InspectionStatus) VALUES (@vin, 'IT12ABC', 2015, 1800, @inspection)",
        ("@vin", vin),
        ("@inspection", (int)inspection));

    /// <summary>
    /// The convoy this vehicle is currently travelling with, as the derived read computes it:
    /// an un-withdrawn truck-list row on a convoy that has not arrived. Null when there is none.
    /// </summary>
    /// <remarks>
    /// This used to read <c>dbo.Vehicle.ConvoyId</c> and come back as <see cref="DBNull"/> for a
    /// free vehicle. There is no column now, so "on no convoy" is no row at all.
    /// </remarks>
    internal static async Task<int?> ConvoyOfAsync(string vin)
    {
        var value = await ValueAsync(
            """
            SELECT TOP 1 cv.ConvoyId
            FROM dbo.ConvoyVehicle AS cv
            INNER JOIN dbo.Convoy AS c ON c.Id = cv.ConvoyId
            WHERE cv.Vin = @vin AND cv.WithdrawnAt IS NULL AND c.ArrivedAt IS NULL
            ORDER BY cv.AddedAt DESC
            """,
            ("@vin", vin));

        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    internal static Task<object?> WithdrawnAtAsync(int convoyId, string vin) => ValueAsync(
        "SELECT WithdrawnAt FROM dbo.ConvoyVehicle WHERE ConvoyId = @id AND Vin = @vin",
        ("@id", convoyId), ("@vin", vin));

    internal static Task<object?> HandedOverAtAsync(string vin) =>
        ValueAsync("SELECT HandedOverAt FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin));

    internal static Task<int> TruckListCountAsync(int convoyId) =>
        ScalarAsync("SELECT COUNT(1) FROM dbo.ConvoyVehicle WHERE ConvoyId = @id", ("@id", convoyId));

    /// <summary>Deletes the vehicle, which cascades its truck-list rows and their crew and insurance.</summary>
    internal static Task RemoveVehicleAsync(string vin) =>
        ExecuteAsync("DELETE FROM dbo.Vehicle WHERE Vin = @vin", ("@vin", vin));

    /// <summary>Takes the truck list out before the convoy, because the foreign key is NO ACTION.</summary>
    internal static Task RemoveConvoyAsync(int id) => ExecuteAsync(
        """
        DELETE FROM dbo.ConvoyVehicle WHERE ConvoyId = @id;
        DELETE FROM dbo.Convoy WHERE Id = @id;
        """,
        ("@id", id));

    internal static Task<Guid> AddDriverAsync(string firstName, string lastName) =>
        AddVolunteerAsync(firstName, lastName, isDriver: true);

    internal static Task RemovePeopleAsync(params Guid[] ids) => Task.WhenAll(ids.Select(id =>
        ExecuteAsync(
            "DELETE FROM dbo.ConvoyVehicleCrew WHERE PersonId = @id; DELETE FROM dbo.Person WHERE Id = @id",
            ("@id", id))));

    /// <summary>
    /// A manifest against a truck-list entry. Both keys are NOT NULL and are a composite foreign
    /// key, so the entry has to exist first — which is the whole point of the change.
    /// </summary>
    internal static async Task<string> AddManifestAsync(int convoyId, string vin, ManifestStatus status)
    {
        var id = "IT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        await ExecuteAsync(
            "INSERT INTO dbo.Manifest (Id, ConvoyId, Vin, Status) VALUES (@id, @convoyId, @vin, @status)",
            ("@id", id), ("@convoyId", convoyId), ("@vin", vin), ("@status", (int)status));
        return id;
    }

    internal static Task RemoveManifestsAsync(int convoyId) =>
        ExecuteAsync("DELETE FROM dbo.Manifest WHERE ConvoyId = @id", ("@id", convoyId));

    internal static VehicleInsuranceRecord AnInsurance(int convoyId, string vin, string policy = "POL-1") => new(
        convoyId, vin, "Ukraine Aid Mutual", policy,
        new DateTime(2026, 8, 25), new DateTime(2026, 9, 30), 412.50m, "operator-sub");
}
