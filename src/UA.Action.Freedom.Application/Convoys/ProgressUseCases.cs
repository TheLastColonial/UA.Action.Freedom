using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>
/// One report of progress as it is read back. It carries the time it was entered and who entered it, never a position
/// (O20, O27).
/// </summary>
public sealed record RoutePointMarkReadModel(
    int RoutePointId,
    RoutePointMarkKind Kind,
    string? Vin,
    string? RecordedByName,
    DateTime RecordedAt,
    bool OnBehalfOfLeader);

public enum RecordMarkResult
{
    Recorded,
    AlreadyMarked,
}

/// <summary>
/// Persistence port for <c>dbo.RoutePointMark</c>. A crossing and the closing of the declarations it covers are one
/// fact, so <see cref="RecordAsync"/> does both in one transaction.
/// </summary>
public interface IRoutePointMarkRepository
{
    Task<IReadOnlyList<RoutePointMarkReadModel>> ListAsync(int convoyId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the mark. When <paramref name="closes"/> is given, every accepted declaration of those kinds for the vehicle
    /// (or for every vehicle still travelling, when <see cref="RoutePointMark.Vin"/> is null) moves to
    /// <see cref="DeclarationStatus.Closed"/> in the same transaction. A mark already made for that point, kind and vehicle
    /// is <see cref="RecordMarkResult.AlreadyMarked"/> and changes nothing.
    /// </summary>
    Task<RecordMarkResult> RecordAsync(
        RoutePointMark mark, IReadOnlyList<DeclarationKind> closes, CancellationToken cancellationToken);
}

/// <summary>Mark a route point reached or crossed. The Convoy Leader's act; the Dispatcher may do it on their behalf (O27).</summary>
public sealed record RecordRoutePointMarkCommand(int ConvoyId, int RoutePointId, RoutePointMarkKind Kind, string? Vin);

public enum RecordRoutePointMarkOutcome
{
    Recorded,
    ConvoyNotFound,
    ConvoyNotDeparted,
    RoutePointNotFound,
    VehicleNotOnConvoy,
    CrossingNeedsBorder,
    AlreadyMarked,
    LoginNotLinked,
}

public sealed class RecordRoutePointMarkHandler(
    IConvoyRepository convoys,
    IConvoyVehicleRepository truckList,
    IConvoyLeaderRepository leaders,
    IRoutePointMarkRepository marks,
    ICurrentPerson currentPerson,
    TimeProvider clock)
    : ICommandHandler<RecordRoutePointMarkCommand, RecordRoutePointMarkOutcome>
{
    public async Task<RecordRoutePointMarkOutcome> HandleAsync(
        RecordRoutePointMarkCommand command, CancellationToken cancellationToken)
    {
        if (await convoys.GetByIdAsync(command.ConvoyId, cancellationToken) is not { } convoy)
        {
            return RecordRoutePointMarkOutcome.ConvoyNotFound;
        }

        if (!convoy.Departed)
        {
            return RecordRoutePointMarkOutcome.ConvoyNotDeparted;
        }

        var route = await convoys.GetRouteAsync(command.ConvoyId, cancellationToken);
        if (route.FirstOrDefault(stop => stop.RoutePointId == command.RoutePointId) is not { } point)
        {
            return RecordRoutePointMarkOutcome.RoutePointNotFound;
        }

        if (RoutePointMarks.Check(point.Kind, command.Kind) != RoutePointMarkCheck.Ok)
        {
            return RecordRoutePointMarkOutcome.CrossingNeedsBorder;
        }

        if (command.Vin is not null
            && await truckList.GetAsync(command.ConvoyId, command.Vin, cancellationToken) is not { Travelling: true })
        {
            return RecordRoutePointMarkOutcome.VehicleNotOnConvoy;
        }

        if (await currentPerson.ResolveAsync(cancellationToken) is not CurrentPerson.Linked(var personId))
        {
            return RecordRoutePointMarkOutcome.LoginNotLinked;
        }

        var leads = await leaders.IsCurrentLeaderAsync(command.ConvoyId, personId, cancellationToken);
        var closes = command.Kind == RoutePointMarkKind.Crossed && point.Authority is { } authority
            ? RoutePointMarks.DeclarationsClosedBy(authority)
            : [];

        var mark = new RoutePointMark(
            command.ConvoyId, command.RoutePointId, command.Kind, command.Vin, personId,
            clock.GetUtcNow().UtcDateTime, OnBehalfOfLeader: !leads);

        return await marks.RecordAsync(mark, closes, cancellationToken) == RecordMarkResult.Recorded
            ? RecordRoutePointMarkOutcome.Recorded
            : RecordRoutePointMarkOutcome.AlreadyMarked;
    }
}

public sealed record ProgressBookingReadModel(string Provider, DateTime CheckIn, DateTime CheckOut);

/// <summary>A route point as the checklist shows it: its name and kind (the header), what has been reported, and where the crew sleeps.</summary>
public sealed record ProgressPointReadModel(
    int RoutePointId,
    int Sequence,
    string Name,
    RoutePointKind Kind,
    CustomsAuthority? Authority,
    IReadOnlyList<RoutePointMarkReadModel> Marks,
    IReadOnlyList<ProgressBookingReadModel> Accommodation);

public sealed record ProgressVehicleReadModel(string Vin, string Plate, bool Delivered);

public sealed record ProgressHubReadModel(int LocationId, string Name);

/// <summary>
/// What the Convoy Leader's checklist and HQ's progress view read. No address of any kind: the route is its points'
/// names and kinds, and the hubs are named, because the page records a return to one (ADR 0016, X13).
/// </summary>
public sealed record ConvoyProgressReadModel(
    int ConvoyId,
    DateTime? DepartedAt,
    DateTime? ArrivedAt,
    IReadOnlyList<ProgressPointReadModel> Points,
    IReadOnlyList<ProgressVehicleReadModel> Vehicles,
    IReadOnlyList<ProgressHubReadModel> Hubs,
    string? HqPhone = null);

public sealed record GetConvoyProgressQuery(int ConvoyId);

public sealed class GetConvoyProgressHandler(
    IConvoyRepository convoys,
    IConvoyVehicleRepository truckList,
    IRoutePointMarkRepository marks,
    IAccommodationRepository accommodation,
    ILocationRepository locations)
    : IQueryHandler<GetConvoyProgressQuery, ConvoyProgressReadModel?>
{
    public async Task<ConvoyProgressReadModel?> HandleAsync(
        GetConvoyProgressQuery query, CancellationToken cancellationToken)
    {
        if (await convoys.GetByIdAsync(query.ConvoyId, cancellationToken) is not { } convoy)
        {
            return null;
        }

        var route = await convoys.GetRouteAsync(query.ConvoyId, cancellationToken);
        var reported = await marks.ListAsync(query.ConvoyId, cancellationToken);
        var bookings = (await accommodation.ListBookingsAsync(query.ConvoyId, cancellationToken))
            .Where(booking => !booking.Cancelled)
            .ToList();
        var vehicles = await truckList.ListAsync(query.ConvoyId, cancellationToken);
        var hubs = await locations.ListAsync(1, 200, LocationVisibility.All, cancellationToken);

        var points = route
            .OrderBy(stop => stop.Sequence)
            .Select(stop => new ProgressPointReadModel(
                stop.RoutePointId,
                stop.Sequence,
                stop.Name,
                stop.Kind,
                stop.Authority,
                [.. reported.Where(mark => mark.RoutePointId == stop.RoutePointId)],
                [.. bookings
                    .Where(booking => booking.RoutePointId == stop.RoutePointId)
                    .Select(booking => new ProgressBookingReadModel(booking.Provider, booking.CheckIn, booking.CheckOut))]))
            .ToList();

        return new ConvoyProgressReadModel(
            convoy.Id,
            convoy.DepartedAt,
            convoy.ArrivedAt,
            points,
            [.. vehicles.Where(vehicle => vehicle.Travelling).Select(vehicle => new ProgressVehicleReadModel(vehicle.Vin, vehicle.Plate, vehicle.Delivered))],
            [.. hubs.Where(hub => hub.IsRegisteredHub).Select(hub => new ProgressHubReadModel(hub.Id, hub.Name))]);
    }
}
