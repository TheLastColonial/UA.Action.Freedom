using Dapper;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Declarations;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Data.Declarations;

/// <summary>Dapper-backed <see cref="IDeclarationRepository"/> over <c>dbo.Declaration</c>.</summary>
/// <remarks>VINs go through <see cref="SqlKey.Of"/>; every transition is a conditional UPDATE.</remarks>
public sealed class DeclarationRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution)
    : IDeclarationRepository
{
    private static readonly string Select =
        $"""
        SELECT d.Id, d.ConvoyId, d.Vin, d.Kind, d.Status, d.ReceiverRef, d.Reference, d.ReasonCode,
               recorder.DisplayName AS RecordedByName, d.RecordedAt, {ChangeStamp.ReadColumns("d")},
               d.SnapshotJson, d.SnapshotVersion
        FROM dbo.Declaration AS d
        LEFT JOIN dbo.PersonDisplay AS recorder ON recorder.PersonId = d.RecordedBy
        {ChangeStamp.ReadJoin("d")}
        """;

    private const string ScopeWhere =
        "d.ConvoyId = @convoyId AND d.Vin = @vin AND d.Kind = @kind AND d.Status <> @withdrawn "
        + "AND ((@receiverRef IS NULL AND d.ReceiverRef IS NULL) OR d.ReceiverRef = @receiverRef)";

    private static object Scope(int convoyId, string vin, DeclarationKind kind, Guid? receiverRef) => new
    {
        convoyId,
        vin = SqlKey.Of(vin),
        kind = (int)kind,
        withdrawn = (int)DeclarationStatus.Withdrawn,
        receiverRef,
    };

    public async Task<IReadOnlyList<DeclarationReadModel>> ListAsync(
        int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<DeclarationReadModel>(new CommandDefinition(
            $"{Select} WHERE d.ConvoyId = @convoyId AND d.Vin = @vin ORDER BY d.Id",
            new { convoyId, vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<DeclarationReadModel?> GetCurrentAsync(
        int convoyId, string vin, DeclarationKind kind, Guid? receiverRef, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<DeclarationReadModel>(new CommandDefinition(
            $"{Select} WHERE {ScopeWhere}",
            Scope(convoyId, vin, kind, receiverRef),
            cancellationToken: cancellationToken));
    }

    public async Task<RecordReferenceResult> RecordReferenceAsync(
        int convoyId, string vin, DeclarationKind kind, Guid? receiverRef, string? reference,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var onList = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.ConvoyVehicle WITH (UPDLOCK) WHERE ConvoyId = @convoyId AND Vin = @vin",
            new { convoyId, vin = SqlKey.Of(vin) },
            transaction,
            cancellationToken: cancellationToken));

        if (onList == 0)
        {
            return RecordReferenceResult.VehicleNotOnConvoy;
        }

        var scope = attribution.With(Scope(convoyId, vin, kind, receiverRef));
        scope.Add("refused", (int)DeclarationStatus.Refused);
        scope.Add("target", (int)DeclarationTransitions.RecordedStatus(kind));
        scope.Add("reference", reference);

        // One statement: take the current declaration if it has no reference yet (a draft, or a refused
        // one that has been corrected), stamping the reference and the new status together. The row
        // count says whether this call was the one that recorded it.
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            $"""
            UPDATE d SET Status = @target, Reference = @reference, ReasonCode = NULL,
                SnapshotJson = CASE WHEN d.Status = @refused THEN NULL ELSE d.SnapshotJson END,
                SnapshotVersion = CASE WHEN d.Status = @refused THEN NULL ELSE d.SnapshotVersion END,
                RecordedBy = @changedBy, RecordedAt = SYSUTCDATETIME(),
                LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
            FROM dbo.Declaration AS d
            WHERE {ScopeWhere} AND (d.Reference IS NULL OR d.Status = @refused)
            """,
            scope,
            transaction,
            cancellationToken: cancellationToken));

        if (updated == 0)
        {
            var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                $"SELECT COUNT(1) FROM dbo.Declaration AS d WHERE {ScopeWhere}",
                scope,
                transaction,
                cancellationToken: cancellationToken));

            if (exists > 0)
            {
                return RecordReferenceResult.AlreadyRecorded;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.Declaration
                    (ConvoyId, Vin, ReceiverRef, Kind, Status, Reference, RecordedBy, RecordedAt, LastChangedBy, LastChangedAt)
                VALUES (@convoyId, @vin, @receiverRef, @kind, @target, @reference, @changedBy, SYSUTCDATETIME(),
                        @changedBy, SYSUTCDATETIME())
                """,
                scope,
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return RecordReferenceResult.Recorded;
    }

    public async Task<bool> RefuseAsync(int id, string reasonCode, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Declaration SET Status = @refused, ReasonCode = @reasonCode,
                LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
            WHERE Id = @id AND Status = @filed
            """,
            attribution.With(new
            {
                id,
                reasonCode,
                filed = (int)DeclarationStatus.Filed,
                refused = (int)DeclarationStatus.Refused,
            }),
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> WithdrawAsync(
        int convoyId, string vin, DeclarationKind kind, Guid? receiverRef, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var scope = attribution.With(Scope(convoyId, vin, kind, receiverRef));
        scope.Add("filed", (int)DeclarationStatus.Filed);
        scope.Add("accepted", (int)DeclarationStatus.Accepted);
        scope.Add("stale", (int)DeclarationStatus.Stale);

        // Invalidation is the resolution of a stale declaration (Filed|Accepted -> Stale -> Withdrawn),
        // taken in one transaction so nothing observes the half-way state. The row and its reference stay.
        var stale = await connection.ExecuteAsync(new CommandDefinition(
            $"""
            UPDATE d SET Status = @stale, LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
            FROM dbo.Declaration AS d
            WHERE {ScopeWhere} AND d.Status IN (@filed, @accepted)
            """,
            scope,
            transaction,
            cancellationToken: cancellationToken));

        if (stale == 0)
        {
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            $"""
            UPDATE d SET Status = @withdrawn, LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
            FROM dbo.Declaration AS d
            WHERE d.ConvoyId = @convoyId AND d.Vin = @vin AND d.Kind = @kind AND d.Status = @stale
              AND ((@receiverRef IS NULL AND d.ReceiverRef IS NULL) OR d.ReceiverRef = @receiverRef)
            """,
            scope,
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<MarkReadyResult> MarkReadyAsync(
        int convoyId, string vin, DeclarationKind kind, Guid? receiverRef, string snapshotJson, int snapshotVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var onList = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.ConvoyVehicle WITH (UPDLOCK) WHERE ConvoyId = @convoyId AND Vin = @vin",
            new { convoyId, vin = SqlKey.Of(vin) },
            transaction,
            cancellationToken: cancellationToken));

        if (onList == 0)
        {
            return MarkReadyResult.VehicleNotOnConvoy;
        }

        var scope = attribution.With(Scope(convoyId, vin, kind, receiverRef));
        scope.Add("draft", (int)DeclarationStatus.Draft);
        scope.Add("ready", (int)DeclarationStatus.ReadyToFile);
        scope.Add("snapshotJson", snapshotJson);
        scope.Add("snapshotVersion", snapshotVersion);

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            $"""
            UPDATE d SET Status = @ready, SnapshotJson = @snapshotJson, SnapshotVersion = @snapshotVersion,
                LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
            FROM dbo.Declaration AS d
            WHERE {ScopeWhere} AND d.Status IN (@draft, @ready)
            """,
            scope,
            transaction,
            cancellationToken: cancellationToken));

        if (updated == 0)
        {
            var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                $"SELECT COUNT(1) FROM dbo.Declaration AS d WHERE {ScopeWhere}",
                scope,
                transaction,
                cancellationToken: cancellationToken));

            if (exists > 0)
            {
                return MarkReadyResult.NotPreparable;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.Declaration
                    (ConvoyId, Vin, ReceiverRef, Kind, Status, SnapshotJson, SnapshotVersion, LastChangedBy, LastChangedAt)
                VALUES (@convoyId, @vin, @receiverRef, @kind, @ready, @snapshotJson, @snapshotVersion, @changedBy,
                        SYSUTCDATETIME())
                """,
                scope,
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return MarkReadyResult.Ready;
    }

    public async Task<bool> StoreSnapshotAsync(
        int convoyId, string vin, DeclarationKind kind, Guid? receiverRef, string snapshotJson, int snapshotVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var scope = attribution.With(Scope(convoyId, vin, kind, receiverRef));
        scope.Add("snapshotJson", snapshotJson);
        scope.Add("snapshotVersion", snapshotVersion);

        var stored = await connection.ExecuteAsync(new CommandDefinition(
            $"""
            UPDATE d SET SnapshotJson = @snapshotJson, SnapshotVersion = @snapshotVersion,
                LastChangedBy = @changedBy, LastChangedAt = SYSUTCDATETIME()
            FROM dbo.Declaration AS d
            WHERE {ScopeWhere} AND d.SnapshotJson IS NULL
            """,
            scope,
            cancellationToken: cancellationToken));

        return stored > 0;
    }
}
