namespace UA.Action.Freedom.Application.Convoys;

/// <summary>
/// A convoy as this slice persists and returns it: when it leaves, when it is expected to
/// arrive, and whether its truck list has been published.
/// </summary>
/// <remarks>
/// The route and the vehicles are sub-resources rather than members here. A convoy list is read
/// far more often than a route is, and a flat row keeps Dapper's constructor mapping honest.
/// </remarks>
public sealed record ConvoyReadModel(
    int Id,
    DateTime Start,
    DateTime ExpectedEnd,
    DateTime? TruckListPublishedAt)
{
    /// <summary>
    /// Whether the set of vehicles is closed. See <c>docs/process.puml</c>: manifests are
    /// proposed against a published truck list, so publication fixes what is on it.
    /// </summary>
    public bool TruckListPublished => this.TruckListPublishedAt is not null;
}

/// <summary>
/// One stop on a convoy's route. <see cref="Sequence"/> is 1-based and dense — the order is the
/// journey, from UK departure to Ukrainian delivery.
/// </summary>
public sealed record RouteStopReadModel(
    int Sequence,
    string? House,
    string? Street,
    string? City,
    string? Country,
    string Postcode);

/// <summary>
/// A vehicle as it appears on a convoy's truck list — enough to recognise it and to add up a
/// border-check weight, not the whole vehicle record.
/// </summary>
public sealed record ConvoyVehicleReadModel(string Vin, string Plate, int WeightKg);
