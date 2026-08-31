using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Manifests;

/// <summary>
/// Persistence port for <see cref="ManifestReadModel"/> and the two things a manifest composes:
/// its driver teams and its cargo.
/// </summary>
public interface IManifestRepository
{
    Task<ManifestReadModel?> GetByIdAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ManifestReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken);

    Task AddAsync(ManifestReadModel manifest, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the vehicle, convoy, notes and ferry booking. Cannot touch the status or the GMR
    /// stamp — those belong to the transitions.
    /// </summary>
    Task<bool> UpdateAsync(ManifestReadModel manifest, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Moves the manifest from <paramref name="from"/> to <paramref name="to"/>, but only if it
    /// is still in <paramref name="from"/>. Returns false when it has moved underneath us, which
    /// is how two people pressing the same button resolve to one transition.
    /// </summary>
    Task<bool> TransitionAsync(string id, ManifestStatus from, ManifestStatus to, CancellationToken cancellationToken);

    /// <summary>
    /// Confirms the manifest and freezes it in one statement, returning the stamp it wrote.
    /// </summary>
    /// <remarks>
    /// The freeze and the status change are the same write because a confirmed manifest whose
    /// GMR is on its way must never be editable, not even for the width of a second statement.
    /// Returns null when the manifest was not in <paramref name="from"/> any more.
    /// </remarks>
    Task<DateTime?> ConfirmAndFreezeAsync(string id, ManifestStatus from, CancellationToken cancellationToken);

    Task<IReadOnlyList<ManifestDriverTeamReadModel>> ListTeamsAsync(string id, CancellationToken cancellationToken);

    Task SetTeamAsync(string id, ManifestDriverTeamReadModel team, CancellationToken cancellationToken);

    Task<IReadOnlyList<ManifestBoxReadModel>> ListBoxesAsync(string id, CancellationToken cancellationToken);

    /// <summary>Returns false when there is no box with that identifier.</summary>
    Task<bool> AddBoxAsync(string id, int boxId, CancellationToken cancellationToken);

    /// <summary>Returns false when that box is not on this manifest.</summary>
    Task<bool> RemoveBoxAsync(string id, int boxId, CancellationToken cancellationToken);

    /// <summary>The kerb weight of the manifest's vehicle, or zero when none is assigned yet.</summary>
    Task<int> GetVehicleWeightKgAsync(string id, CancellationToken cancellationToken);
}

/// <summary>
/// The durable hand-off from the API to the Customs Worker.
/// </summary>
/// <remarks>
/// A port so that approving a manifest can be tested without a storage account. The message
/// carries a manifest reference and vehicle registration and <strong>no receiver name, contact
/// or address</strong> — the worker has no business knowing where in Ukraine a load is going,
/// and its logs are retained (recommendations §4.1, §4.4).
/// </remarks>
public interface IManifestWorkQueue
{
    Task EnqueueGmrSubmissionAsync(GmrSubmissionRequest submission, CancellationToken cancellationToken);
}

/// <param name="ManifestId">Which manifest this movement is for.</param>
/// <param name="VehicleRegistration">The plate the border expects to see.</param>
/// <param name="DepartsAt">
/// Planned departure, taken from the convoy. HMRC needs a crossing time, and the convoy is what
/// knows it — the manifest only knows which convoy it is on.
/// </param>
public sealed record GmrSubmissionRequest(string ManifestId, string VehicleRegistration, DateTime? DepartsAt);
