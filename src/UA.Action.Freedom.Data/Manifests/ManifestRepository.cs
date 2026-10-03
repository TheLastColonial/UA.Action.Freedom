using Dapper;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Data.Manifests;

/// <summary>
/// Dapper-backed <see cref="IManifestRepository"/> over <c>dbo.Manifest</c> and
/// <c>dbo.ManifestBox</c>.
/// </summary>
/// <remarks>
/// There is no driver-team table any more: crew is read from <c>dbo.ConvoyVehicleCrew</c> through
/// the truck list, so this repository never writes a person.
/// </remarks>
public sealed class ManifestRepository(IDbConnectionFactory connectionFactory, IChangeAttribution attribution) : IManifestRepository
{
    private static readonly string Columns =
        $"m.Id, m.ConvoyId, m.Vin, m.Status, m.DeliveryNotes, m.FerryBookingComplete, m.GmrSubmittedAt, {ChangeStamp.ReadColumns("m")}";

    private static readonly string From = $"dbo.Manifest AS m {ChangeStamp.ReadJoin("m")}";

    public async Task<ManifestReadModel?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<ManifestReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM {From} WHERE m.Id = @id",
            new { id = SqlKey.Of(id) },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ManifestReadModel>> ListAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<ManifestReadModel>(new CommandDefinition(
            $"""
             SELECT {Columns} FROM {From}
             ORDER BY m.Id
             OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
             """,
            new { skip = (page - 1) * pageSize, take = pageSize },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.Manifest WHERE Id = @id",
            new { id = SqlKey.Of(id) },
            cancellationToken: cancellationToken));

        return count > 0;
    }

    public async Task<ManifestReadModel?> GetForVehicleAsync(
        int convoyId, string vin, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<ManifestReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM {From} WHERE m.ConvoyId = @convoyId AND m.Vin = @vin",
            new { convoyId, vin = SqlKey.Of(vin) },
            cancellationToken: cancellationToken));
    }

    public async Task AddAsync(ManifestReadModel manifest, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.Manifest (Id, ConvoyId, Vin, Status, DeliveryNotes, FerryBookingComplete, LastChangedBy, LastChangedAt)
            VALUES (CAST(@Id AS varchar(32)), @ConvoyId, CAST(@Vin AS varchar(32)),
                    @Status, @DeliveryNotes, @FerryBookingComplete, @changedBy, SYSUTCDATETIME())
            """,
            attribution.With(manifest),
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(ManifestReadModel manifest, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // ConvoyId, Vin, Status and GmrSubmittedAt are all absent on purpose. The first two are
        // the manifest's identity — the truck-list entry it is the paperwork for — and the
        // lifecycle belongs to the transitions, so there is no way to re-point a manifest at
        // another vehicle, or to un-freeze it, through an edit.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Manifest SET
                DeliveryNotes = @DeliveryNotes,
                FerryBookingComplete = @FerryBookingComplete,
                UpdatedAt = SYSUTCDATETIME(),
                LastChangedBy = @changedBy,
                LastChangedAt = SYSUTCDATETIME()
            WHERE Id = CAST(@Id AS varchar(32))
            """,
            attribution.With(manifest),
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // The cargo links cascade; the boxes themselves are untouched.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.Manifest WHERE Id = @id",
            new { id = SqlKey.Of(id) },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> TransitionAsync(
        string id, ManifestStatus from, ManifestStatus to, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Conditional on the manifest still being in the state we read, so the database settles
        // a race between two dispatchers rather than the last write winning.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Manifest SET
                Status = @to,
                UpdatedAt = SYSUTCDATETIME(),
                LastChangedBy = @changedBy,
                LastChangedAt = SYSUTCDATETIME()
            WHERE Id = @id AND Status = @from
            """,
            attribution.With(new { id = SqlKey.Of(id), from = (int)from, to = (int)to }),
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<DateTime?> ConfirmAndFreezeAsync(
        string id, ManifestStatus from, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // One statement, and it returns what it wrote. Confirming and freezing cannot be two
        // writes: a manifest that is Confirmed but not yet frozen is editable, and that window
        // is exactly what recommendations §5.2 forbids.
        return await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            """
            UPDATE dbo.Manifest SET
                Status = @confirmed,
                GmrSubmittedAt = SYSUTCDATETIME(),
                UpdatedAt = SYSUTCDATETIME(),
                LastChangedBy = @changedBy,
                LastChangedAt = SYSUTCDATETIME()
            OUTPUT INSERTED.GmrSubmittedAt
            WHERE Id = @id AND Status = @from AND GmrSubmittedAt IS NULL
            """,
            attribution.With(new { id = SqlKey.Of(id), from = (int)from, confirmed = (int)ManifestStatus.Confirmed }),
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ManifestBoxReadModel>> ListBoxesAsync(
        string id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // The box's own weight and validation state come along, because a manifest weight built
        // from anything else would be a number nobody had confirmed.
        var rows = await connection.QueryAsync<ManifestBoxReadModel>(new CommandDefinition(
            """
            SELECT b.Id AS BoxId,
                   b.WeightKg,
                   CAST(CASE WHEN b.ValidatedAt IS NULL THEN 0 ELSE 1 END AS bit) AS Validated,
                   b.WidthCm,
                   b.DepthCm,
                   b.HeightCm
            FROM dbo.ManifestBox AS mb
            INNER JOIN dbo.Box AS b ON b.Id = mb.BoxId
            WHERE mb.ManifestId = @id
            ORDER BY b.Id
            """,
            new { id = SqlKey.Of(id) },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<bool> AddBoxAsync(string id, int boxId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // A box travels on at most one manifest, so this moves it rather than duplicating it.
        // The same box counted on two manifests would be declared twice and arrive once.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ManifestBox SET ManifestId = @id WHERE BoxId = @boxId;

            IF @@ROWCOUNT = 0 AND EXISTS (SELECT 1 FROM dbo.Box WHERE Id = @boxId)
                INSERT INTO dbo.ManifestBox (BoxId, ManifestId) VALUES (@boxId, @id);
            """,
            new { id = SqlKey.Of(id), boxId },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> RemoveBoxAsync(string id, int boxId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Scoped to this manifest: taking a box off one it was never on is a caller mistake.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.ManifestBox WHERE BoxId = @boxId AND ManifestId = @id",
            new { id = SqlKey.Of(id), boxId },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<int> GetVehicleWeightKgAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Zero only when there is no such manifest: every manifest names a vehicle now, so a
        // partial answer here means the caller asked about something that does not exist.
        return await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            SELECT v.WeightKg
            FROM dbo.Manifest AS m
            INNER JOIN dbo.Vehicle AS v ON v.Vin = m.Vin
            WHERE m.Id = @id
            """,
            new { id = SqlKey.Of(id) },
            cancellationToken: cancellationToken)) ?? 0;
    }

    public async Task<string?> GetVehiclePlateAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // The plate, not the VIN. Both the GMR submission and the printed document say
        // "registration" and were being handed the chassis number, which is not what a border
        // officer reads off the front of the vehicle.
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT v.Plate
            FROM dbo.Manifest AS m
            INNER JOIN dbo.Vehicle AS v ON v.Vin = m.Vin
            WHERE m.Id = @id
            """,
            new { id = SqlKey.Of(id) },
            cancellationToken: cancellationToken));
    }

    public async Task<VehicleCargoCapacityReadModel> GetVehicleCargoCapacityAsync(
        string id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // All-null when the vehicle's capacity has never been measured — nothing back-fills it,
        // and a vehicle nobody has measured simply cannot be judged overloaded.
        return await connection.QuerySingleOrDefaultAsync<VehicleCargoCapacityReadModel>(new CommandDefinition(
            """
            SELECT v.MaxCargoWeightKg, v.CargoWidthCm, v.CargoDepthCm, v.CargoHeightCm
            FROM dbo.Manifest AS m
            INNER JOIN dbo.Vehicle AS v ON v.Vin = m.Vin
            WHERE m.Id = @id
            """,
            new { id = SqlKey.Of(id) },
            cancellationToken: cancellationToken)) ?? new VehicleCargoCapacityReadModel(null, null, null, null);
    }

    public async Task<IReadOnlyList<ManifestDocumentLineReadModel>> GetDocumentLinesAsync(
        string id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // dbo.Receiver only — organisation and region. sensitive.ReceiverDetail is not joined
        // and could not be: this connection is DENY'd on that schema, so a query here that
        // reached for a delivery address would fail at the database (recommendations §4.4).
        var rows = await connection.QueryAsync<ManifestDocumentLineReadModel>(new CommandDefinition(
            """
            SELECT b.Id                                          AS BoxId,
                   b.WeightKg,
                   (SELECT COUNT(1) FROM dbo.BoxItem AS i WHERE i.BoxId = b.Id) AS ItemCount,
                   r.Organisation                                AS ReceiverOrganisation,
                   r.Region                                      AS ReceiverRegion
            FROM dbo.ManifestBox AS mb
            INNER JOIN dbo.Box AS b ON b.Id = mb.BoxId
            LEFT JOIN dbo.Receiver AS r ON r.ReceiverRef = b.ReceiverRef
            WHERE mb.ManifestId = @id
            ORDER BY b.Id
            """,
            new { id = SqlKey.Of(id) },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<IReadOnlyList<EnsGoodsLineReadModel>> GetEnsGoodsLinesAsync(
        string id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // A row per item, because an ENS declares goods items while Freedom packs boxes. The box's
        // weight repeats across its items; the caller de-duplicates on BoxId before adding anything
        // up, which is also how it counts packages.
        //
        // dbo.Receiver only, as in GetDocumentLinesAsync — and ReceiverRef is included here because
        // the sheet groups by consignee and tells the filer which receiver to look the address up
        // against. The address itself is in the sensitive schema this connection is DENY'd on (§4.4).
        //
        // ORDER BY is not cosmetic: the goods item number is non-amendable in ICS2, so it has to come
        // from a stable order rather than whatever the database felt like returning.
        var rows = await connection.QueryAsync<EnsGoodsLineReadModel>(new CommandDefinition(
            """
            SELECT b.Id             AS BoxId,
                   b.WeightKg,
                   CAST(CASE WHEN b.ValidatedAt IS NULL THEN 0 ELSE 1 END AS bit) AS Validated,
                   b.ReceiverRef,
                   r.Organisation   AS ReceiverOrganisation,
                   r.Region         AS ReceiverRegion,
                   i.Description    AS ItemDescription,
                   i.CommodityCode
            FROM dbo.ManifestBox AS mb
            INNER JOIN dbo.Box AS b ON b.Id = mb.BoxId
            INNER JOIN dbo.BoxItem AS i ON i.BoxId = b.Id
            LEFT JOIN dbo.Receiver AS r ON r.ReceiverRef = b.ReceiverRef
            WHERE mb.ManifestId = @id
            ORDER BY b.Id, i.Description, i.Id
            """,
            new { id = SqlKey.Of(id) },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }
}
