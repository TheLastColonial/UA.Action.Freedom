using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Data.Convoys;

/// <summary>
/// Dapper-backed <see cref="IRoutePointMarkRepository"/> over <c>dbo.RoutePointMark</c>. A crossing is one transaction:
/// the mark, and every accepted declaration it covers moving to Closed. A closed declaration is frozen and is never
/// compared for staleness, so the crossing and the freezing must not be able to part.
/// </summary>
public sealed class RoutePointMarkRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution)
    : IRoutePointMarkRepository
{
    public async Task<IReadOnlyList<RoutePointMarkReadModel>> ListAsync(int convoyId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<RoutePointMarkReadModel>(new CommandDefinition(
            """
            SELECT m.RoutePointId, m.Kind, m.Vin, who.DisplayName AS RecordedByName, m.RecordedAt, m.OnBehalfOfLeader
            FROM dbo.RoutePointMark AS m
            LEFT JOIN dbo.PersonDisplay AS who ON who.PersonId = m.RecordedBy
            WHERE m.ConvoyId = @convoyId
            ORDER BY m.RecordedAt, m.Id
            """,
            new { convoyId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<RecordMarkResult> RecordAsync(
        RoutePointMark mark, IReadOnlyList<DeclarationKind> closes, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.RoutePointMark (ConvoyId, RoutePointId, Kind, Vin, OnBehalfOfLeader, RecordedBy, RecordedAt)
                VALUES (@ConvoyId, @RoutePointId, @Kind, @vin, @OnBehalfOfLeader, @RecordedBy, @RecordedAt)
                """,
                InsertParameters(mark),
                transaction,
                cancellationToken: cancellationToken));

            if (closes.Count > 0)
            {
                // Only an accepted declaration closes (Accepted -> Closed is the one edge into that state). A vehicle that
                // has left the convoy, or one named by no mark, is not crossing and keeps its declarations as they are.
                var vehicleFilter = mark.Vin is null ? string.Empty : "AND d.Vin = @vin";
                await connection.ExecuteAsync(new CommandDefinition(
                    $"""
                    UPDATE d
                    SET Status = @closed, LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
                    FROM dbo.Declaration AS d
                    INNER JOIN dbo.ConvoyVehicle AS cv ON cv.ConvoyId = d.ConvoyId AND cv.Vin = d.Vin
                    WHERE d.ConvoyId = @convoyId
                      AND d.Status = @accepted
                      AND d.Kind IN (SELECT value FROM OPENJSON(@kinds) WITH (value int '$'))
                      AND cv.WithdrawnAt IS NULL
                      {vehicleFilter}
                    """,
                    CloseParameters(mark, closes),
                    transaction,
                    cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            return RecordMarkResult.Recorded;
        }
        catch (SqlException exception)
            when (exception.Number is SqlErrors.UniqueIndexViolation or SqlErrors.UniqueConstraintViolation)
        {
            return RecordMarkResult.AlreadyMarked;
        }
    }

    // VIN is a varchar(32) key: it is sent as a varchar so the comparison does not convert the column. A null VIN (a mark on
    // the whole convoy) is sent as a typed null, which an ICustomQueryParameter such as SqlKey.Of cannot be.
    private static DynamicParameters InsertParameters(RoutePointMark mark)
    {
        var parameters = new DynamicParameters(new
        {
            mark.ConvoyId,
            mark.RoutePointId,
            Kind = (int)mark.Kind,
            mark.OnBehalfOfLeader,
            mark.RecordedBy,
            mark.RecordedAt,
        });
        parameters.Add("vin", mark.Vin, DbType.AnsiString, size: 32);
        return parameters;
    }

    private DynamicParameters CloseParameters(RoutePointMark mark, IReadOnlyList<DeclarationKind> closes)
    {
        var parameters = attribution.With(new
        {
            convoyId = mark.ConvoyId,
            closed = (int)DeclarationStatus.Closed,
            accepted = (int)DeclarationStatus.Accepted,
            kinds = System.Text.Json.JsonSerializer.Serialize(closes.Select(kind => (int)kind)),
        });

        if (mark.Vin is not null)
        {
            parameters.Add("vin", mark.Vin, DbType.AnsiString, size: 32);
        }

        return parameters;
    }
}
