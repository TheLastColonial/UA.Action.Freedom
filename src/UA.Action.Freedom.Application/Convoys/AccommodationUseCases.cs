using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Declarations;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>A crew member of the convoy, as the accommodation grid names them.</summary>
public sealed record AccommodationCrewReadModel(Guid PersonId, string Name);

/// <summary>A route point the grid has a column for: an overnight stop, in route order.</summary>
public sealed record AccommodationStopReadModel(int RoutePointId, int Sequence, string Name);

public sealed record AccommodationCellReadModel(int RoutePointId, Guid PersonId, CoverageStatus Status);

/// <summary>A booking that covers nobody who is still crewed (P13): the Dispatcher cancels or migrates it (P16).</summary>
public sealed record LeftoverBookingReadModel(int BookingId, int RoutePointId, IReadOnlyList<Guid> GuestIds);

/// <summary>
/// The coverage grid: every crew member at every overnight stop (P8). Read in one go, never one call per cell.
/// <see cref="Warnings"/> are advice and never block departure (P16); plan 13 reads <see cref="AllCovered"/> as a
/// requirement.
/// </summary>
public sealed record AccommodationCoverageReadModel(
    IReadOnlyList<AccommodationStopReadModel> Stops,
    IReadOnlyList<AccommodationCrewReadModel> Crew,
    IReadOnlyList<AccommodationCellReadModel> Cells,
    int MissingCount,
    bool AllCovered,
    IReadOnlyList<LeftoverBookingReadModel> LeftoverBookings,
    IReadOnlyList<string> Warnings);

/// <summary>The bookings of a convoy and the nights its crew arrange themselves.</summary>
public sealed record AccommodationReadModel(
    IReadOnlyList<AccommodationBookingReadModel> Bookings,
    IReadOnlyList<SelfAccommodationReadModel> SelfArranged);

/// <summary>
/// Who is crewed on a convoy, as a person is for accommodation: seated on a vehicle that is still travelling with it.
/// A person on a vehicle that withdrew left with it.
/// </summary>
internal static class ConvoyCrew
{
    public static async Task<IReadOnlyList<VehicleCrewReadModel>> OfAsync(
        IConvoyVehicleRepository truckList, int convoyId, CancellationToken cancellationToken)
    {
        // One read per vehicle, as readiness does: a convoy is a handful of vans.
        var crew = new List<VehicleCrewReadModel>();
        foreach (var vehicle in (await truckList.ListAsync(convoyId, cancellationToken)).Where(vehicle => vehicle.Travelling))
        {
            crew.AddRange(await truckList.ListCrewAsync(convoyId, vehicle.Vin, cancellationToken) ?? []);
        }

        return crew;
    }
}

internal static class AccommodationOverview
{
    public static IReadOnlyList<RoutePoint> OvernightPoints(IEnumerable<RouteStopReadModel> route) =>
        [.. route
            .Where(stop => stop.Kind == RoutePointKind.Overnight)
            .Select(stop => new RoutePoint(stop.RoutePointId, stop.Sequence, stop.Name, stop.Kind, new Address()))];

    public static AccommodationBooking Domain(AccommodationBookingReadModel booking) =>
        new(booking.Id, booking.RoutePointId, booking.Guests, booking.Cancelled);

    public static async Task<AccommodationCoverageReadModel> OfAsync(
        IConvoyRepository convoys,
        IConvoyVehicleRepository truckList,
        IAccommodationRepository accommodation,
        int convoyId,
        CancellationToken cancellationToken)
    {
        var route = await convoys.GetRouteAsync(convoyId, cancellationToken);
        var crew = (await ConvoyCrew.OfAsync(truckList, convoyId, cancellationToken))
            .DistinctBy(member => member.PersonId)
            .ToList();
        var bookings = await accommodation.ListBookingsAsync(convoyId, cancellationToken);
        var selfArranged = await accommodation.ListSelfAsync(convoyId, cancellationToken);

        var points = OvernightPoints(route);
        var crewIds = crew.Select(member => member.PersonId).ToList();
        var cells = Accommodation.Coverage(
            points,
            crewIds,
            [.. bookings.Select(Domain)],
            [.. selfArranged.Select(flag => new SelfArrangement(flag.RoutePointId, flag.PersonId))]);

        var leftover = Accommodation.LeftoverBookings(bookings.Select(Domain), crewIds)
            .Select(booking => new LeftoverBookingReadModel(booking.Id, booking.RoutePointId, [.. booking.Guests]))
            .ToList();

        var names = route.ToDictionary(stop => stop.RoutePointId, stop => stop.Name);
        var missing = cells.Count(cell => cell.Status == CoverageStatus.Missing);

        return new AccommodationCoverageReadModel(
            [.. points.Select(point => new AccommodationStopReadModel(point.Id, point.Sequence, point.Name))],
            [.. crew.Select(member => new AccommodationCrewReadModel(member.PersonId, $"{member.FirstName} {member.LastName}"))],
            [.. cells.Select(cell => new AccommodationCellReadModel(cell.RoutePointId, cell.PersonId, cell.Status))],
            missing,
            missing == 0,
            leftover,
            [.. leftover.Select(booking =>
                $"Booking {booking.BookingId} at {names.GetValueOrDefault(booking.RoutePointId, "a removed stop")} covers nobody still crewed: cancel or migrate it")]);
    }
}

public sealed record GetAccommodationQuery(int ConvoyId);

public sealed class GetAccommodationHandler(IConvoyRepository convoys, IAccommodationRepository accommodation)
    : IQueryHandler<GetAccommodationQuery, AccommodationReadModel?>
{
    public async Task<AccommodationReadModel?> HandleAsync(GetAccommodationQuery query, CancellationToken cancellationToken) =>
        await convoys.GetByIdAsync(query.ConvoyId, cancellationToken) is null
            ? null
            : new AccommodationReadModel(
                await accommodation.ListBookingsAsync(query.ConvoyId, cancellationToken),
                await accommodation.ListSelfAsync(query.ConvoyId, cancellationToken));
}

public sealed record GetAccommodationCoverageQuery(int ConvoyId);

public sealed class GetAccommodationCoverageHandler(
    IConvoyRepository convoys, IConvoyVehicleRepository truckList, IAccommodationRepository accommodation)
    : IQueryHandler<GetAccommodationCoverageQuery, AccommodationCoverageReadModel?>
{
    public async Task<AccommodationCoverageReadModel?> HandleAsync(
        GetAccommodationCoverageQuery query, CancellationToken cancellationToken) =>
        await convoys.GetByIdAsync(query.ConvoyId, cancellationToken) is null
            ? null
            : await AccommodationOverview.OfAsync(convoys, truckList, accommodation, query.ConvoyId, cancellationToken);
}

public sealed record BookAccommodationCommand(AccommodationBookingRecord Booking);

public enum BookAccommodationOutcome
{
    Booked,
    ConvoyNotFound,
    ConvoyArrived,
    PointNotOnConvoy,
    GuestNotOnCrew
}

public sealed record BookAccommodationResult(BookAccommodationOutcome Outcome, int? Id = null);

/// <summary>Book a stay for crew at a route point. Its guests must be crewed on the convoy now.</summary>
public sealed class BookAccommodationHandler(
    IConvoyRepository convoys, IConvoyVehicleRepository truckList, IAccommodationRepository accommodation)
    : ICommandHandler<BookAccommodationCommand, BookAccommodationResult>
{
    public async Task<BookAccommodationResult> HandleAsync(
        BookAccommodationCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.Booking.ConvoyId, cancellationToken);
        if (convoy is null)
        {
            return new BookAccommodationResult(BookAccommodationOutcome.ConvoyNotFound);
        }

        if (convoy.Arrived)
        {
            return new BookAccommodationResult(BookAccommodationOutcome.ConvoyArrived);
        }

        var crew = (await ConvoyCrew.OfAsync(truckList, command.Booking.ConvoyId, cancellationToken))
            .Select(member => member.PersonId)
            .ToHashSet();

        if (!command.Booking.Guests.All(crew.Contains))
        {
            return new BookAccommodationResult(BookAccommodationOutcome.GuestNotOnCrew);
        }

        return await accommodation.AddBookingAsync(command.Booking, cancellationToken) is { } id
            ? new BookAccommodationResult(BookAccommodationOutcome.Booked, id)
            : new BookAccommodationResult(BookAccommodationOutcome.PointNotOnConvoy);
    }
}

public sealed record ReplaceAccommodationCommand(int BookingId, AccommodationBookingRecord Booking);

public enum ReplaceAccommodationOutcome
{
    Replaced,
    ConvoyNotFound,
    ConvoyArrived,
    NotFound,
    Cancelled,
    PointNotOnConvoy,
    GuestNotOnCrew
}

/// <summary>
/// Replace a booking. A guest already on it may stay even if they have left the crew (their booking stays, P13), but
/// nobody uncrewed can be added.
/// </summary>
public sealed class ReplaceAccommodationHandler(
    IConvoyRepository convoys, IConvoyVehicleRepository truckList, IAccommodationRepository accommodation)
    : ICommandHandler<ReplaceAccommodationCommand, ReplaceAccommodationOutcome>
{
    public async Task<ReplaceAccommodationOutcome> HandleAsync(
        ReplaceAccommodationCommand command, CancellationToken cancellationToken)
    {
        var convoyId = command.Booking.ConvoyId;
        var convoy = await convoys.GetByIdAsync(convoyId, cancellationToken);
        if (convoy is null)
        {
            return ReplaceAccommodationOutcome.ConvoyNotFound;
        }

        if (convoy.Arrived)
        {
            return ReplaceAccommodationOutcome.ConvoyArrived;
        }

        var existing = await accommodation.GetBookingAsync(convoyId, command.BookingId, cancellationToken);
        if (existing is null)
        {
            return ReplaceAccommodationOutcome.NotFound;
        }

        if (existing.Cancelled)
        {
            return ReplaceAccommodationOutcome.Cancelled;
        }

        var allowed = (await ConvoyCrew.OfAsync(truckList, convoyId, cancellationToken))
            .Select(member => member.PersonId)
            .Concat(existing.Guests)
            .ToHashSet();

        if (!command.Booking.Guests.All(allowed.Contains))
        {
            return ReplaceAccommodationOutcome.GuestNotOnCrew;
        }

        return await accommodation.ReplaceBookingAsync(command.BookingId, command.Booking, cancellationToken) switch
        {
            ReplaceBookingResult.Replaced => ReplaceAccommodationOutcome.Replaced,
            ReplaceBookingResult.Cancelled => ReplaceAccommodationOutcome.Cancelled,
            ReplaceBookingResult.PointNotOnConvoy => ReplaceAccommodationOutcome.PointNotOnConvoy,
            _ => ReplaceAccommodationOutcome.NotFound,
        };
    }
}

public sealed record CancelAccommodationCommand(int ConvoyId, int BookingId);

public enum CancelAccommodationOutcome
{
    Cancelled,
    ConvoyNotFound,
    NotFound
}

/// <summary>Cancel a booking, the Dispatcher's answer to a leftover one. The booking and its cost stay on record.</summary>
public sealed class CancelAccommodationHandler(IConvoyRepository convoys, IAccommodationRepository accommodation)
    : ICommandHandler<CancelAccommodationCommand, CancelAccommodationOutcome>
{
    public async Task<CancelAccommodationOutcome> HandleAsync(
        CancelAccommodationCommand command, CancellationToken cancellationToken)
    {
        if (await convoys.GetByIdAsync(command.ConvoyId, cancellationToken) is null)
        {
            return CancelAccommodationOutcome.ConvoyNotFound;
        }

        return await accommodation.CancelBookingAsync(command.ConvoyId, command.BookingId, cancellationToken)
            ? CancelAccommodationOutcome.Cancelled
            : CancelAccommodationOutcome.NotFound;
    }
}

public sealed record MigrateAccommodationCommand(int ConvoyId, int BookingId, Guid FromPersonId, Guid ToPersonId);

public enum MigrateAccommodationOutcome
{
    Migrated,
    ConvoyNotFound,
    ConvoyArrived,
    NotFound,
    Cancelled,
    NotAGuest,
    ReplacementNotOnCrew
}

/// <summary>Hand a departed guest's place on a booking to their replacement, who must be crewed now (P13).</summary>
public sealed class MigrateAccommodationHandler(
    IConvoyRepository convoys, IConvoyVehicleRepository truckList, IAccommodationRepository accommodation)
    : ICommandHandler<MigrateAccommodationCommand, MigrateAccommodationOutcome>
{
    public async Task<MigrateAccommodationOutcome> HandleAsync(
        MigrateAccommodationCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.ConvoyId, cancellationToken);
        if (convoy is null)
        {
            return MigrateAccommodationOutcome.ConvoyNotFound;
        }

        if (convoy.Arrived)
        {
            return MigrateAccommodationOutcome.ConvoyArrived;
        }

        var crewed = (await ConvoyCrew.OfAsync(truckList, command.ConvoyId, cancellationToken))
            .Any(member => member.PersonId == command.ToPersonId);

        if (!crewed)
        {
            return MigrateAccommodationOutcome.ReplacementNotOnCrew;
        }

        return await accommodation.MigrateGuestAsync(
            command.ConvoyId, command.BookingId, command.FromPersonId, command.ToPersonId, cancellationToken) switch
        {
            MigrateGuestResult.Migrated => MigrateAccommodationOutcome.Migrated,
            MigrateGuestResult.Cancelled => MigrateAccommodationOutcome.Cancelled,
            MigrateGuestResult.NotAGuest => MigrateAccommodationOutcome.NotAGuest,
            _ => MigrateAccommodationOutcome.NotFound,
        };
    }
}

public sealed record SetSelfAccommodationCommand(int ConvoyId, int RoutePointId, Guid PersonId);

public enum SetSelfAccommodationOutcome
{
    Set,
    ConvoyNotFound,
    ConvoyArrived,
    PointNotOnConvoy,
    NotOvernight,
    PersonNotOnCrew
}

/// <summary>Flag a crew member as arranging their own stay at an overnight stop (O4, O30).</summary>
public sealed class SetSelfAccommodationHandler(
    IConvoyRepository convoys, IConvoyVehicleRepository truckList, IAccommodationRepository accommodation)
    : ICommandHandler<SetSelfAccommodationCommand, SetSelfAccommodationOutcome>
{
    public async Task<SetSelfAccommodationOutcome> HandleAsync(
        SetSelfAccommodationCommand command, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(command.ConvoyId, cancellationToken);
        if (convoy is null)
        {
            return SetSelfAccommodationOutcome.ConvoyNotFound;
        }

        if (convoy.Arrived)
        {
            return SetSelfAccommodationOutcome.ConvoyArrived;
        }

        var stop = (await convoys.GetRouteAsync(command.ConvoyId, cancellationToken))
            .FirstOrDefault(point => point.RoutePointId == command.RoutePointId);

        if (stop is null)
        {
            return SetSelfAccommodationOutcome.PointNotOnConvoy;
        }

        if (stop.Kind != RoutePointKind.Overnight)
        {
            return SetSelfAccommodationOutcome.NotOvernight;
        }

        var crewed = (await ConvoyCrew.OfAsync(truckList, command.ConvoyId, cancellationToken))
            .Any(member => member.PersonId == command.PersonId);

        if (!crewed)
        {
            return SetSelfAccommodationOutcome.PersonNotOnCrew;
        }

        return await accommodation.SetSelfAsync(command.ConvoyId, command.RoutePointId, command.PersonId, cancellationToken)
            ? SetSelfAccommodationOutcome.Set
            : SetSelfAccommodationOutcome.PointNotOnConvoy;
    }
}

public sealed record ClearSelfAccommodationCommand(int ConvoyId, int RoutePointId, Guid PersonId);

public enum ClearSelfAccommodationOutcome
{
    Cleared,
    ConvoyNotFound,
    NotFound
}

public sealed class ClearSelfAccommodationHandler(IConvoyRepository convoys, IAccommodationRepository accommodation)
    : ICommandHandler<ClearSelfAccommodationCommand, ClearSelfAccommodationOutcome>
{
    public async Task<ClearSelfAccommodationOutcome> HandleAsync(
        ClearSelfAccommodationCommand command, CancellationToken cancellationToken)
    {
        if (await convoys.GetByIdAsync(command.ConvoyId, cancellationToken) is null)
        {
            return ClearSelfAccommodationOutcome.ConvoyNotFound;
        }

        return await accommodation.RemoveSelfAsync(command.ConvoyId, command.RoutePointId, command.PersonId, cancellationToken)
            ? ClearSelfAccommodationOutcome.Cleared
            : ClearSelfAccommodationOutcome.NotFound;
    }
}

/// <summary>What the Dispatcher does about a leftover booking (P13, P16).</summary>
public enum LeftoverBookingResolution
{
    /// <summary>Cancel it (and try for a refund), or migrate it to a replacement.</summary>
    CancelOrMigrate,
}

/// <summary>
/// A leftover booking as an on-screen Dispatcher task (O21): derived, so there is nothing to close. Cancelling the
/// booking, or migrating it to someone crewed, clears it. Carries identifiers only.
/// </summary>
public sealed record LeftoverBookingTaskReadModel(
    int BookingId, int RoutePointId, IReadOnlyList<Guid> GuestIds, LeftoverBookingResolution Resolution)
{
    public string Type => "accommodation-leftover";
}

/// <summary>Every task on a convoy: re-declare tasks from stale declarations, and leftover bookings.</summary>
public sealed record ListConvoyTasksQuery(int ConvoyId);

public sealed class ListConvoyTasksHandler(
    IQueryHandler<ListRedeclareTasksQuery, IReadOnlyList<RedeclareTaskReadModel>?> redeclare,
    IConvoyRepository convoys,
    IConvoyVehicleRepository truckList,
    IAccommodationRepository accommodation)
    : IQueryHandler<ListConvoyTasksQuery, IReadOnlyList<object>?>
{
    public async Task<IReadOnlyList<object>?> HandleAsync(ListConvoyTasksQuery query, CancellationToken cancellationToken)
    {
        if (await redeclare.HandleAsync(new ListRedeclareTasksQuery(query.ConvoyId), cancellationToken) is not { } redeclareTasks)
        {
            return null;
        }

        var overview = await AccommodationOverview.OfAsync(convoys, truckList, accommodation, query.ConvoyId, cancellationToken);

        return
        [
            .. redeclareTasks,
            .. overview.LeftoverBookings.Select(booking => new LeftoverBookingTaskReadModel(
                booking.BookingId, booking.RoutePointId, booking.GuestIds, LeftoverBookingResolution.CancelOrMigrate)),
        ];
    }
}
