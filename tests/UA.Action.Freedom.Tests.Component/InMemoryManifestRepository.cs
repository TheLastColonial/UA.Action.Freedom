using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Dictionary-backed manifest persistence so the endpoint tests run without a database.
/// </summary>
internal sealed class InMemoryManifestRepository : IManifestRepository
{
    private readonly Dictionary<string, ManifestReadModel> manifests = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ManifestDriverTeamReadModel>> teams = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ManifestBoxReadModel>> boxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> knownBoxes = [];

    private int vehicleWeightKg;

    public InMemoryManifestRepository(params ManifestReadModel[] seed)
    {
        foreach (var manifest in seed)
        {
            manifests[manifest.Id] = manifest;
        }
    }

    public InMemoryManifestRepository WithVehicleWeight(int weightKg)
    {
        vehicleWeightKg = weightKg;
        return this;
    }

    public InMemoryManifestRepository WithKnownBox(int boxId)
    {
        knownBoxes.Add(boxId);
        return this;
    }

    public InMemoryManifestRepository WithBoxOn(string manifestId, ManifestBoxReadModel box)
    {
        knownBoxes.Add(box.BoxId);
        boxes.TryAdd(manifestId, []);
        boxes[manifestId].Add(box);
        return this;
    }

    public int Count => manifests.Count;

    public ManifestReadModel? Manifest(string id) => manifests.GetValueOrDefault(id);

    public IReadOnlyList<ManifestDriverTeamReadModel> Teams(string id) => teams.GetValueOrDefault(id, []);

    public IReadOnlyList<ManifestBoxReadModel> Boxes(string id) => boxes.GetValueOrDefault(id, []);

    public Task<ManifestReadModel?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(manifests.GetValueOrDefault(id));

    public Task<IReadOnlyList<ManifestReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ManifestReadModel>>(
            manifests.Values
                .OrderBy(manifest => manifest.Id, StringComparer.Ordinal)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList());

    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(manifests.ContainsKey(id));

    public Task AddAsync(ManifestReadModel manifest, CancellationToken cancellationToken)
    {
        manifests[manifest.Id] = manifest;
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(ManifestReadModel manifest, CancellationToken cancellationToken)
    {
        if (!manifests.TryGetValue(manifest.Id, out var existing))
        {
            return Task.FromResult(false);
        }

        // Mirrors the SQL, which cannot reach the status or the GMR stamp on an update.
        manifests[manifest.Id] = manifest with
        {
            Status = existing.Status,
            GmrSubmittedAt = existing.GmrSubmittedAt,
        };

        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        teams.Remove(id);
        boxes.Remove(id);
        return Task.FromResult(manifests.Remove(id));
    }

    public Task<bool> TransitionAsync(
        string id, ManifestStatus from, ManifestStatus to, CancellationToken cancellationToken)
    {
        if (!manifests.TryGetValue(id, out var manifest) || manifest.Status != from)
        {
            return Task.FromResult(false);
        }

        manifests[id] = manifest with { Status = to };
        return Task.FromResult(true);
    }

    public Task<DateTime?> ConfirmAndFreezeAsync(string id, ManifestStatus from, CancellationToken cancellationToken)
    {
        if (!manifests.TryGetValue(id, out var manifest) || manifest.Status != from || manifest.Frozen)
        {
            return Task.FromResult<DateTime?>(null);
        }

        var stamped = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);
        manifests[id] = manifest with { Status = ManifestStatus.Confirmed, GmrSubmittedAt = stamped };

        return Task.FromResult<DateTime?>(stamped);
    }

    public Task<IReadOnlyList<ManifestDriverTeamReadModel>> ListTeamsAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ManifestDriverTeamReadModel>>(
            teams.GetValueOrDefault(id, []).OrderBy(team => team.Leg).ToList());

    public Task SetTeamAsync(string id, ManifestDriverTeamReadModel team, CancellationToken cancellationToken)
    {
        teams.TryAdd(id, []);
        teams[id].RemoveAll(existing => existing.Leg == team.Leg);
        teams[id].Add(team);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ManifestBoxReadModel>> ListBoxesAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ManifestBoxReadModel>>(boxes.GetValueOrDefault(id, []));

    public Task<bool> AddBoxAsync(string id, int boxId, CancellationToken cancellationToken)
    {
        if (!knownBoxes.Contains(boxId))
        {
            return Task.FromResult(false);
        }

        // A box travels on at most one manifest, so this moves it.
        foreach (var cargo in boxes.Values)
        {
            cargo.RemoveAll(box => box.BoxId == boxId);
        }

        boxes.TryAdd(id, []);
        boxes[id].Add(new ManifestBoxReadModel(boxId, 0, Validated: false));

        return Task.FromResult(true);
    }

    public Task<bool> RemoveBoxAsync(string id, int boxId, CancellationToken cancellationToken) =>
        Task.FromResult(boxes.TryGetValue(id, out var cargo) && cargo.RemoveAll(box => box.BoxId == boxId) > 0);

    public Task<int> GetVehicleWeightKgAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(vehicleWeightKg);
}

/// <summary>
/// Captures what would have gone on the customs work queue, so the endpoint tests can assert
/// that approving a manifest hands off exactly one submission — and that nothing else does.
/// </summary>
internal sealed class RecordingManifestWorkQueue : IManifestWorkQueue
{
    public List<GmrSubmissionRequest> Submissions { get; } = [];

    public Task EnqueueGmrSubmissionAsync(GmrSubmissionRequest submission, CancellationToken cancellationToken)
    {
        Submissions.Add(submission);
        return Task.CompletedTask;
    }
}
