using FluentValidation;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Api.Convoys;

/// <summary>Body of <c>POST /convoys</c>. The identifier is assigned by the database.</summary>
/// <remarks>
/// <paramref name="CrossingMode"/> defaults to <see cref="ChannelCrossing.Ferry"/> so existing callers
/// keep working, but it is worth supplying: it decides the ENS mode-of-transport code, and a ferry
/// crossing also needs <paramref name="VesselImo"/>. Both are non-amendable in ICS2 once the
/// declaration is filed.
/// </remarks>
public sealed record CreateConvoyRequest(
    DateTime Start,
    DateTime ExpectedEnd,
    ChannelCrossing? CrossingMode = null,
    string? VesselImo = null)
{
    public CreateConvoyCommand ToCommand() =>
        new(Start, ExpectedEnd, CrossingMode ?? ChannelCrossing.Ferry, VesselImo);
}

/// <summary>
/// Body of <c>PUT /convoys/{id}</c>. The route supplies the identifier, and the truck list's
/// publication is not settable here — it has its own endpoint.
/// </summary>
public sealed record UpdateConvoyRequest(
    DateTime Start,
    DateTime ExpectedEnd,
    ChannelCrossing? CrossingMode = null,
    string? VesselImo = null)
{
    public UpdateConvoyCommand ToCommand(int id) =>
        new(id, Start, ExpectedEnd, CrossingMode ?? ChannelCrossing.Ferry, VesselImo);
}

/// <summary>One stop in a <c>PUT /convoys/{id}/route</c> body. Position in the list is the order.</summary>
/// <param name="CountryCode">
/// The ISO 3166-1 alpha-2 code for <paramref name="Country"/>. An ENS declares its countries of
/// routing as codes, and EU customs cannot complete its pre-arrival risk assessment without every
/// country the goods pass through — so it is supplied rather than inferred from free text.
/// </param>
public sealed record RouteStopRequest(
    string? House,
    string? Street,
    string? City,
    string? Country,
    string Postcode,
    string? CountryCode = null);

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
            index + 1, stop.House, stop.Street, stop.City, stop.Country, stop.Postcode,
            stop.CountryCode))]);
}

/// <summary>
/// Body of <c>PUT /convoys/{id}/vehicles/{vin}/handover-receiver</c>: the registered Receiver the vehicle is
/// handed over to in Ukraine.
/// </summary>
public sealed record SetHandoverReceiverRequest(Guid ReceiverRef)
{
    public SetHandoverReceiverCommand ToCommand(int convoyId, string vin) => new(convoyId, vin, ReceiverRef);
}

public sealed class SetHandoverReceiverRequestValidator : AbstractValidator<SetHandoverReceiverRequest>
{
    public SetHandoverReceiverRequestValidator()
    {
        RuleFor(r => r.ReceiverRef).NotEmpty();
    }
}

/// <summary>
/// Body of <c>PUT /convoys/{id}/vehicles/{vin}/crew/{personId}</c>.
/// </summary>
/// <remarks>
/// A seat is one person on one vehicle on one convoy, so there is no leg to say. The role is optional and defaults to <see cref="CrewRole.Driver"/>, which is what most crewing
/// is.
/// </remarks>
public sealed record AssignCrewRequest(CrewRole? Role = null);

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
/// caller's linked login; a <c>recordedBy</c> in the body is ignored.
/// </summary>
public sealed record RecordInsuranceRequest(
    string Insurer,
    string PolicyNumber,
    DateTime CoverStart,
    DateTime CoverEnd,
    decimal? CostGbp = null)
{
    public RecordInsuranceCommand ToCommand(int convoyId, string vin, Guid recordedBy) =>
        new(new VehicleInsuranceRecord(
            convoyId, vin, Insurer, PolicyNumber, CoverStart.Date, CoverEnd.Date, CostGbp, recordedBy));
}
