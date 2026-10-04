using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>
/// A convoy as this slice persists and returns it: when it leaves, when it is expected to
/// arrive, and whether its truck list has been published.
/// </summary>
/// <remarks>
/// The route and the truck list are sub-resources rather than members here. A convoy list is read
/// far more often than a route is, and a flat row keeps Dapper's constructor mapping honest.
/// </remarks>
public sealed record ConvoyReadModel(
    int Id,
    DateTime Start,
    DateTime ExpectedEnd,
    DateTime? TruckListPublishedAt,
    DateTime? ArrivedAt = null,
    ChannelCrossing CrossingMode = ChannelCrossing.Ferry,
    string? VesselImo = null,
    string? LastChangedByName = null,
    DateTime? LastChangedAt = null)
{
    /// <summary>
    /// Whether the convoy has arrived. After that nothing about it changes — its vehicles are
    /// handed over or free to travel again, and its crew and insurance are history.
    /// </summary>
    public bool Arrived => this.ArrivedAt is not null;

    /// <summary>
    /// Whether the set of vehicles is closed to additions. See <c>docs/process.puml</c>: manifests
    /// are proposed against a published truck list, so publication fixes what is on it. A vehicle
    /// may still be <em>withdrawn</em> afterwards — see <see cref="ConvoyVehicleReadModel"/>.
    /// </summary>
    public bool TruckListPublished => this.TruckListPublishedAt is not null;
}

/// <summary>
/// One stop on a convoy's route. <see cref="Sequence"/> is 1-based and dense — the order is the
/// journey, from UK departure to Ukrainian delivery.
/// </summary>
/// <param name="CountryCode">
/// The ISO 3166-1 alpha-2 code for <paramref name="Country"/>, which is free text written by a
/// dispatcher planning a journey. An ENS declares its countries of routing as codes, and missing one
/// stops EU customs completing its pre-arrival risk assessment, so the code is stored beside the name
/// rather than guessed from it at filing time.
/// </param>
/// <param name="RoutePointId">
/// The point's stable identity, assigned by the database. <c>0</c> on a point not stored yet; a route edit that
/// names an existing id updates that point in place, so whatever refers to it keeps pointing at it.
/// </param>
/// <param name="Kind">What the point is for. Only a <see cref="RoutePointKind.Border"/> has an <paramref name="Authority"/>.</param>
public sealed record RouteStopReadModel(
    int Sequence,
    string? House,
    string? Street,
    string? City,
    string? Country,
    string Postcode,
    string? CountryCode = null,
    int RoutePointId = 0,
    string Name = "",
    RoutePointKind Kind = RoutePointKind.Stop,
    CustomsAuthority? Authority = null);

/// <summary>
/// A vehicle as it appears on a convoy's truck list — enough to recognise it, to add up a
/// border-check weight and to judge whether it is crewed, not the whole vehicle record.
/// </summary>
/// <remarks>
/// A vehicle needs one <em>driver</em> and is advised two; passengers do not count towards either.
///
/// <para>
/// <see cref="WithdrawnAt"/> is set when the vehicle left the convoy mid-journey — a breakdown,
/// most often. The row stays on the list, because its manifest and its Goods Movement Reference
/// still describe a real load and the record of which convoy it set off with is part of what
/// happened.
/// </para>
/// </remarks>
public sealed record ConvoyVehicleReadModel(
    string Vin,
    string Plate,
    int WeightKg,
    int DriverCount,
    int PassengerCount,
    DateTime? WithdrawnAt = null,
    string? WithdrawnReason = null,
    Guid? HandoverReceiverRef = null)
{
    /// <summary>Whether the vehicle is still travelling with the convoy.</summary>
    public bool Travelling => ConvoyVehicle.IsTravelling(this.WithdrawnAt);

    /// <summary>Whether the vehicle has left the convoy.</summary>
    public bool Withdrawn => !this.Travelling;
}

/// <summary>
/// A crew member of a vehicle on one convoy.
/// </summary>
/// <remarks>
/// This is the only crew record in the system. The manifest used to keep its own primary/secondary
/// driver teams alongside it, unconnected — so the printed document could name one crew while the
/// insurance, which is what actually gates departure, covered another.
/// </remarks>
public sealed record VehicleCrewReadModel(
    Guid PersonId,
    string FirstName,
    string LastName,
    CrewRole Role);
