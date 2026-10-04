using System.Data.Common;
using Dapper;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Data.Convoys;

/// <summary>
/// Dapper-backed <see cref="IAccommodationRepository"/> over <c>dbo.AccommodationBooking</c>,
/// <c>dbo.AccommodationBookingGuest</c> and <c>dbo.SelfAccommodation</c>.
/// </summary>
public sealed class AccommodationRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution)
    : IAccommodationRepository
{
    private sealed record BookingRow(
        int Id,
        int ConvoyId,
        int RoutePointId,
        string Provider,
        string? Reference,
        DateTime CheckIn,
        DateTime CheckOut,
        string? Details,
        decimal? CostGbp,
        bool Cancelled,
        string? LastChangedByName,
        DateTime? LastChangedAt);

    private sealed record GuestRow(int BookingId, Guid PersonId);

    public async Task<IReadOnlyList<AccommodationBookingReadModel>> ListBookingsAsync(
        int convoyId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        return await ReadBookingsAsync(connection, convoyId, null, cancellationToken);
    }

    public async Task<AccommodationBookingReadModel?> GetBookingAsync(
        int convoyId, int bookingId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        return (await ReadBookingsAsync(connection, convoyId, bookingId, cancellationToken)).SingleOrDefault();
    }

    private static async Task<IReadOnlyList<AccommodationBookingReadModel>> ReadBookingsAsync(
        DbConnection connection, int convoyId, int? bookingId, CancellationToken cancellationToken)
    {
        // Two reads for the whole convoy, never one per booking.
        var rows = (await connection.QueryAsync<BookingRow>(new CommandDefinition(
            $"""
             SELECT b.Id, b.ConvoyId, b.RoutePointId, b.Provider, b.Reference, b.CheckIn, b.CheckOut, b.Details,
                    b.CostGbp, b.Cancelled, {ChangeStamp.ReadColumns("b")}
             FROM dbo.AccommodationBooking AS b {ChangeStamp.ReadJoin("b")}
             WHERE b.ConvoyId = @convoyId AND (@bookingId IS NULL OR b.Id = @bookingId)
             ORDER BY b.RoutePointId, b.Id
             """,
            new { convoyId, bookingId },
            cancellationToken: cancellationToken))).ToList();

        var guests = (await connection.QueryAsync<GuestRow>(new CommandDefinition(
            """
            SELECT g.BookingId, g.PersonId
            FROM dbo.AccommodationBookingGuest AS g
            INNER JOIN dbo.AccommodationBooking AS b ON b.Id = g.BookingId
            WHERE b.ConvoyId = @convoyId AND (@bookingId IS NULL OR b.Id = @bookingId)
            ORDER BY g.BookingId, g.PersonId
            """,
            new { convoyId, bookingId },
            cancellationToken: cancellationToken))).ToLookup(guest => guest.BookingId, guest => guest.PersonId);

        return rows
            .Select(row => new AccommodationBookingReadModel(
                row.Id, row.ConvoyId, row.RoutePointId, row.Provider, row.Reference, row.CheckIn, row.CheckOut,
                row.Details, row.CostGbp, row.Cancelled, [.. guests[row.Id]], row.LastChangedByName, row.LastChangedAt))
            .ToList();
    }

    private static async Task<bool> PointIsOnConvoyAsync(
        DbConnection connection, DbTransaction transaction, int convoyId, int routePointId, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM dbo.ConvoyRouteStop WITH (UPDLOCK, HOLDLOCK)
                WHERE ConvoyId = @convoyId AND RoutePointId = @routePointId) THEN 1 ELSE 0 END AS bit)
            """,
            new { convoyId, routePointId },
            transaction,
            cancellationToken: cancellationToken));

    private static Task InsertGuestsAsync(
        DbConnection connection, DbTransaction transaction, int bookingId, IEnumerable<Guid> guests, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO dbo.AccommodationBookingGuest (BookingId, PersonId) VALUES (@bookingId, @personId)",
            guests.Distinct().Select(personId => new { bookingId, personId }),
            transaction,
            cancellationToken: cancellationToken));

    public async Task<int?> AddBookingAsync(AccommodationBookingRecord booking, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await PointIsOnConvoyAsync(connection, transaction, booking.ConvoyId, booking.RoutePointId, cancellationToken))
        {
            return null;
        }

        var id = await connection.QuerySingleAsync<int>(new CommandDefinition(
            """
            INSERT INTO dbo.AccommodationBooking
                (ConvoyId, RoutePointId, Provider, Reference, CheckIn, CheckOut, Details, CostGbp, LastChangedBy, LastChangedAt)
            OUTPUT INSERTED.Id
            VALUES (@convoyId, @routePointId, @provider, @reference, @checkIn, @checkOut, @details, @costGbp,
                    @changedBy, SYSUTCDATETIME())
            """,
            attribution.With(Parameters(booking)),
            transaction,
            cancellationToken: cancellationToken));

        await InsertGuestsAsync(connection, transaction, id, booking.Guests, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<ReplaceBookingResult> ReplaceBookingAsync(
        int bookingId, AccommodationBookingRecord booking, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var cancelled = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
            "SELECT Cancelled FROM dbo.AccommodationBooking WITH (UPDLOCK, HOLDLOCK) WHERE Id = @bookingId AND ConvoyId = @convoyId",
            new { bookingId, convoyId = booking.ConvoyId },
            transaction,
            cancellationToken: cancellationToken));

        if (cancelled is null)
        {
            return ReplaceBookingResult.NotFound;
        }

        if (cancelled is true)
        {
            return ReplaceBookingResult.Cancelled;
        }

        if (!await PointIsOnConvoyAsync(connection, transaction, booking.ConvoyId, booking.RoutePointId, cancellationToken))
        {
            return ReplaceBookingResult.PointNotOnConvoy;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.AccommodationBooking SET
                RoutePointId = @routePointId, Provider = @provider, Reference = @reference, CheckIn = @checkIn,
                CheckOut = @checkOut, Details = @details, CostGbp = @costGbp,
                LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
            WHERE Id = @bookingId AND ConvoyId = @convoyId
            """,
            attribution.With(Parameters(booking, bookingId)),
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.AccommodationBookingGuest WHERE BookingId = @bookingId",
            new { bookingId },
            transaction,
            cancellationToken: cancellationToken));

        await InsertGuestsAsync(connection, transaction, bookingId, booking.Guests, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return ReplaceBookingResult.Replaced;
    }

    public async Task<bool> CancelBookingAsync(int convoyId, int bookingId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Cancelling twice is not an error, so the stamp is only moved the first time.
        var found = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            UPDATE dbo.AccommodationBooking SET
                Cancelled = 1, LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
            WHERE Id = @bookingId AND ConvoyId = @convoyId AND Cancelled = 0;

            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM dbo.AccommodationBooking WHERE Id = @bookingId AND ConvoyId = @convoyId) THEN 1 ELSE 0 END AS bit)
            """,
            attribution.With(new { convoyId, bookingId }),
            cancellationToken: cancellationToken));

        return found;
    }

    public async Task<MigrateGuestResult> MigrateGuestAsync(
        int convoyId, int bookingId, Guid fromPersonId, Guid toPersonId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        // Taking one guest off and putting another on is one fact: a failure between the two would leave a
        // room with nobody in it, or two people on one place.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var cancelled = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
            "SELECT Cancelled FROM dbo.AccommodationBooking WITH (UPDLOCK, HOLDLOCK) WHERE Id = @bookingId AND ConvoyId = @convoyId",
            new { bookingId, convoyId },
            transaction,
            cancellationToken: cancellationToken));

        if (cancelled is null)
        {
            return MigrateGuestResult.NotFound;
        }

        if (cancelled is true)
        {
            return MigrateGuestResult.Cancelled;
        }

        var removed = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.AccommodationBookingGuest WHERE BookingId = @bookingId AND PersonId = @fromPersonId",
            new { bookingId, fromPersonId },
            transaction,
            cancellationToken: cancellationToken));

        if (removed == 0)
        {
            return MigrateGuestResult.NotAGuest;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            IF NOT EXISTS (SELECT 1 FROM dbo.AccommodationBookingGuest WHERE BookingId = @bookingId AND PersonId = @toPersonId)
                INSERT INTO dbo.AccommodationBookingGuest (BookingId, PersonId) VALUES (@bookingId, @toPersonId)
            """,
            new { bookingId, toPersonId },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.AccommodationBooking SET LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
            WHERE Id = @bookingId
            """,
            attribution.With(new { bookingId }),
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return MigrateGuestResult.Migrated;
    }

    public async Task<IReadOnlyList<SelfAccommodationReadModel>> ListSelfAsync(
        int convoyId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<SelfAccommodationReadModel>(new CommandDefinition(
            $"""
             SELECT s.ConvoyId, s.RoutePointId, s.PersonId, {ChangeStamp.ReadColumns("s")}
             FROM dbo.SelfAccommodation AS s {ChangeStamp.ReadJoin("s")}
             WHERE s.ConvoyId = @convoyId
             ORDER BY s.RoutePointId, s.PersonId
             """,
            new { convoyId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<bool> SetSelfAsync(
        int convoyId, int routePointId, Guid personId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await PointIsOnConvoyAsync(connection, transaction, convoyId, routePointId, cancellationToken))
        {
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            IF NOT EXISTS (
                SELECT 1 FROM dbo.SelfAccommodation WITH (UPDLOCK, HOLDLOCK)
                WHERE ConvoyId = @convoyId AND RoutePointId = @routePointId AND PersonId = @personId)
                INSERT INTO dbo.SelfAccommodation (ConvoyId, RoutePointId, PersonId, LastChangedBy, LastChangedAt)
                VALUES (@convoyId, @routePointId, @personId, @changedBy, SYSUTCDATETIME())
            """,
            attribution.With(new { convoyId, routePointId, personId }),
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveSelfAsync(
        int convoyId, int routePointId, Guid personId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.SelfAccommodation WHERE ConvoyId = @convoyId AND RoutePointId = @routePointId AND PersonId = @personId",
            new { convoyId, routePointId, personId },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    private static object Parameters(AccommodationBookingRecord booking, int? bookingId = null) => new
    {
        bookingId,
        convoyId = booking.ConvoyId,
        routePointId = booking.RoutePointId,
        provider = booking.Provider,
        reference = booking.Reference,
        checkIn = booking.CheckIn,
        checkOut = booking.CheckOut,
        details = booking.Details,
        costGbp = booking.CostGbp,
    };
}

/// <summary>
/// Answers <see cref="IRoutePointReferences"/> for accommodation: a point a booking, cancelled or not, or a
/// self-accommodation flag stays at cannot be removed from the route. The foreign keys say the same, so a race
/// is refused by the database; this is how the answer becomes a 409 rather than a 500.
/// </summary>
public sealed class AccommodationRoutePointReferences(IDbConnectionFactory connectionFactory) : IRoutePointReferences
{
    public async Task<bool> AnyAsync(
        int convoyId, IReadOnlyCollection<int> routePointIds, CancellationToken cancellationToken)
    {
        if (routePointIds.Count == 0)
        {
            return false;
        }

        await using var connection = connectionFactory.Create();

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT CAST(CASE WHEN
                EXISTS (SELECT 1 FROM dbo.AccommodationBooking
                        WHERE ConvoyId = @convoyId AND RoutePointId IN (SELECT value FROM OPENJSON(@ids) WITH (value int '$')))
                OR EXISTS (SELECT 1 FROM dbo.SelfAccommodation
                        WHERE ConvoyId = @convoyId AND RoutePointId IN (SELECT value FROM OPENJSON(@ids) WITH (value int '$')))
                THEN 1 ELSE 0 END AS bit)
            """,
            new { convoyId, ids = System.Text.Json.JsonSerializer.Serialize(routePointIds) },
            cancellationToken: cancellationToken));
    }
}
