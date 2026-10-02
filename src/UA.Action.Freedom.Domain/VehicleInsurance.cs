namespace UA.Action.Freedom.Domain;

/// <summary>
/// The insurance bought for one <see cref="Vehicle"/> on one <see cref="Convoy"/>, covering the
/// crew named on it when it was bought.
/// </summary>
/// <remarks>
/// The policy names the drivers it covers. Removing a driver leaves it in cover for the rest; a
/// driver added afterwards is uncovered until the Dispatcher records it again
/// (<see cref="CoversAllDrivers"/>). <see cref="VoidedAt"/> is an explicit void, not set by crew
/// changes. A manifest cannot depart unless its vehicle's insurance is recorded, not voided, in
/// cover on the day and names every driver.
/// </remarks>
public sealed class VehicleInsurance
{
    public required ConvoyId ConvoyId { get; init; }

    public required string Vin { get; init; }

    public required string Insurer { get; init; }

    public required string PolicyNumber { get; init; }

    public DateTime CoverStart { get; init; }

    public DateTime CoverEnd { get; init; }

    public decimal? CostGbp { get; init; }

    /// <summary>The token subject of whoever recorded it — never taken from a request body.</summary>
    public required string RecordedBy { get; init; }

    public DateTime RecordedAt { get; init; }

    /// <summary>Set when the policy was explicitly voided.</summary>
    public DateTime? VoidedAt { get; init; }

    public bool CoversOn(DateTime day) => InCover(CoverStart, CoverEnd, VoidedAt, day);

    /// <summary>The one statement of when a policy covers a vehicle — shared with the read side.</summary>
    public static bool InCover(DateTime coverStart, DateTime coverEnd, DateTime? voidedAt, DateTime day) =>
        voidedAt is null && coverStart.Date <= day.Date && day.Date <= coverEnd.Date;

    /// <summary>Whether every driver on the crew is named on the policy. Extra covered names do not matter.</summary>
    public static bool CoversAllDrivers(IReadOnlyCollection<Guid> drivers, IReadOnlyCollection<Guid> covered) =>
        drivers.All(covered.Contains);
}
