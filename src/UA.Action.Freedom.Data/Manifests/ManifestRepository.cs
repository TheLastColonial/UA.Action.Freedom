using Dapper;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Data.Manifests;

/// <summary>
/// Dapper-backed <see cref="IManifestRepository"/> over <c>dbo.Manifest</c>,
/// <c>dbo.ManifestDriverTeam</c> and <c>dbo.ManifestBox</c>.
/// </summary>
public sealed class ManifestRepository(IDbConnectionFactory connectionFactory) : IManifestRepository
{
    private const string Columns =
        "Id, Vin, ConvoyId, Status, DeliveryNotes, FerryBookingComplete, GmrSubmittedAt";

    public async Task<ManifestReadModel?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<ManifestReadModel>(new CommandDefinition(
            $"SELECT {Columns} FROM dbo.Manifest WHERE Id = @id",
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ManifestReadModel>> ListAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<ManifestReadModel>(new CommandDefinition(
            $"""
             SELECT {Columns} FROM dbo.Manifest
             ORDER BY Id
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
            new { id },
            cancellationToken: cancellationToken));

        return count > 0;
    }

    public async Task AddAsync(ManifestReadModel manifest, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.Manifest (Id, Vin, ConvoyId, Status, DeliveryNotes, FerryBookingComplete)
            VALUES (@Id, @Vin, @ConvoyId, @Status, @DeliveryNotes, @FerryBookingComplete)
            """,
            manifest,
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(ManifestReadModel manifest, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Status and GmrSubmittedAt are absent on purpose. The lifecycle belongs to the
        // transitions, and there is no way to reach it — or to un-freeze — through an edit.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Manifest SET
                Vin = @Vin,
                ConvoyId = @ConvoyId,
                DeliveryNotes = @DeliveryNotes,
                FerryBookingComplete = @FerryBookingComplete,
                UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id
            """,
            manifest,
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Teams and the cargo links cascade; the boxes themselves are untouched.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.Manifest WHERE Id = @id",
            new { id },
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
                UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @id AND Status = @from
            """,
            new { id, from = (int)from, to = (int)to },
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
                UpdatedAt = SYSUTCDATETIME()
            OUTPUT INSERTED.GmrSubmittedAt
            WHERE Id = @id AND Status = @from AND GmrSubmittedAt IS NULL
            """,
            new { id, from = (int)from, confirmed = (int)ManifestStatus.Confirmed },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ManifestDriverTeamReadModel>> ListTeamsAsync(
        string id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        var rows = await connection.QueryAsync<ManifestDriverTeamReadModel>(new CommandDefinition(
            """
            SELECT Leg, PrimaryPersonId, SecondaryPersonId
            FROM dbo.ManifestDriverTeam
            WHERE ManifestId = @id
            ORDER BY Leg
            """,
            new { id },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task SetTeamAsync(
        string id, ManifestDriverTeamReadModel team, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Assigning a team to a leg replaces whoever was on it. The primary key is
        // (ManifestId, Leg), so a manifest can never accumulate two crews for one leg.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ManifestDriverTeam SET
                PrimaryPersonId = @PrimaryPersonId,
                SecondaryPersonId = @SecondaryPersonId
            WHERE ManifestId = @id AND Leg = @Leg
            """,
            new { id, Leg = (int)team.Leg, team.PrimaryPersonId, team.SecondaryPersonId },
            cancellationToken: cancellationToken));

        if (affected == 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.ManifestDriverTeam (ManifestId, Leg, PrimaryPersonId, SecondaryPersonId)
                VALUES (@id, @Leg, @PrimaryPersonId, @SecondaryPersonId)
                """,
                new { id, Leg = (int)team.Leg, team.PrimaryPersonId, team.SecondaryPersonId },
                cancellationToken: cancellationToken));
        }
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
                   CAST(CASE WHEN b.ValidatedAt IS NULL THEN 0 ELSE 1 END AS bit) AS Validated
            FROM dbo.ManifestBox AS mb
            INNER JOIN dbo.Box AS b ON b.Id = mb.BoxId
            WHERE mb.ManifestId = @id
            ORDER BY b.Id
            """,
            new { id },
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
            new { id, boxId },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> RemoveBoxAsync(string id, int boxId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Scoped to this manifest: taking a box off one it was never on is a caller mistake.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.ManifestBox WHERE BoxId = @boxId AND ManifestId = @id",
            new { id, boxId },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<int> GetVehicleWeightKgAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();

        // Zero when no vehicle is assigned yet: a manifest is Created before it is populated,
        // and asking for its weight then should give a partial answer rather than fail.
        return await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            SELECT v.WeightKg
            FROM dbo.Manifest AS m
            INNER JOIN dbo.Vehicle AS v ON v.Vin = m.Vin
            WHERE m.Id = @id
            """,
            new { id },
            cancellationToken: cancellationToken)) ?? 0;
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
            new { id },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }
}
