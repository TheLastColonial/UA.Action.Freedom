using System.Data;
using Dapper;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Data.Manifests;

/// <summary>
/// The one statement that sends an approved manifest back for re-approval when its vehicle's load changes
/// (ADR 0004, X3).
/// </summary>
/// <remarks>
/// It is run by the repository that changes the load, inside that repository's transaction, so a load that
/// changed and a sign-off that still says it did not can never both be committed. It is conditional on the
/// manifest being <see cref="ManifestStatus.Approved"/>, so a manifest nobody has signed off is left alone and
/// the database settles a race with an approval. <c>ReopenedAt</c> is what lets the screen say why.
/// </remarks>
internal static class ManifestReopening
{
    internal static Task<int> ReopenManifestAsync(
        this IDbConnection connection,
        IDbTransaction transaction,
        IChangeAttribution attribution,
        int convoyId,
        string vin,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Manifest SET
                Status = @proposed,
                ReopenedAt = SYSUTCDATETIME(),
                UpdatedAt = SYSUTCDATETIME(),
                LastChangedBy = @changedBy,
                LastChangedAt = SYSUTCDATETIME()
            WHERE ConvoyId = @convoyId AND Vin = @vin AND Status = @approved
            """,
            attribution.With(new
            {
                convoyId,
                vin = SqlKey.Of(vin),
                proposed = (int)ManifestStatus.Proposed,
                approved = (int)ManifestStatus.Approved,
            }),
            transaction,
            cancellationToken: cancellationToken));
}
