using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Api.Convoys;

/// <summary>Body of <c>POST /convoys</c>. The identifier is assigned by the database.</summary>
public sealed record CreateConvoyRequest(DateTime Start, DateTime ExpectedEnd)
{
    public CreateConvoyCommand ToCommand() => new(Start, ExpectedEnd);
}

/// <summary>
/// Body of <c>PUT /convoys/{id}</c>. The route supplies the identifier, and the truck list's
/// publication is not settable here — it has its own endpoint.
/// </summary>
public sealed record UpdateConvoyRequest(DateTime Start, DateTime ExpectedEnd)
{
    public UpdateConvoyCommand ToCommand(int id) => new(id, Start, ExpectedEnd);
}

/// <summary>One stop in a <c>PUT /convoys/{id}/route</c> body. Position in the list is the order.</summary>
public sealed record RouteStopRequest(
    string? House,
    string? Street,
    string? City,
    string? Country,
    string Postcode);

/// <summary>
/// Body of <c>PUT /convoys/{id}/route</c> — the whole journey, replaced in one go.
/// </summary>
public sealed record ReplaceConvoyRouteRequest(IReadOnlyList<RouteStopRequest> Stops)
{
    /// <summary>
    /// Sequence numbers are assigned from list position here and re-derived by the handler; the
    /// caller never supplies them, so a route cannot arrive with duplicates or gaps.
    /// </summary>
    public ReplaceConvoyRouteCommand ToCommand(int convoyId) => new(
        convoyId,
        [.. Stops.Select((stop, index) => new RouteStopReadModel(
            index + 1, stop.House, stop.Street, stop.City, stop.Country, stop.Postcode))]);
}

/// <summary>
/// Body of <c>PUT /convoys/{id}/vehicles/{vin}/crew/{personId}</c>.
/// </summary>
/// <remarks>
/// The leg is required: a vehicle is crewed twice, once out of the UK and once into Ukraine, and
/// guessing which half somebody is driving is exactly the ambiguity this consolidation removes.
/// The role is optional and defaults to <see cref="CrewRole.Driver"/>, which is what most crewing
/// is.
/// </remarks>
public sealed record AssignCrewRequest(JourneyLeg Leg, CrewRole? Role = null);

/// <summary>
/// Body of <c>POST /convoys/{id}/vehicles/{vin}/manifest</c>. The convoy and the vehicle come from
/// the route — they are the truck-list entry the manifest is the paperwork for — so only the
/// document reference and its contents are here.
/// </summary>
public sealed record CreateConvoyVehicleManifestRequest(
    string Id, string? DeliveryNotes = null, bool FerryBookingComplete = false)
{
    public CreateManifestCommand ToCommand(int convoyId, string vin) =>
        new(Id, convoyId, vin, DeliveryNotes, FerryBookingComplete);
}

/// <summary>
/// Body of <c>PUT /convoys/{id}/vehicles/{vin}/insurance</c>. Who recorded it comes from the
/// caller's token; a <c>recordedBy</c> in the body is ignored.
/// </summary>
public sealed record RecordInsuranceRequest(
    string Insurer,
    string PolicyNumber,
    DateTime CoverStart,
    DateTime CoverEnd,
    decimal? CostGbp = null)
{
    public RecordInsuranceCommand ToCommand(int convoyId, string vin, string recordedBy) =>
        new(new VehicleInsuranceRecord(
            convoyId, vin, Insurer, PolicyNumber, CoverStart.Date, CoverEnd.Date, CostGbp, recordedBy));
}
