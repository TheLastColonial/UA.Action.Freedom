using UA.Action.Freedom.Application.Convoys;

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
