using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Dictionary-backed manifest persistence so the endpoint tests run without a database.
/// </summary>
internal sealed class InMemoryManifestRepository : IManifestRepository, IRecordsWhoChanged
{
    private readonly ChangeLedger<string> changes = new(StringComparer.OrdinalIgnoreCase);

    public void Attach(IChangeAttribution attribution, IPersonRepository people) =>
        changes.Attach(attribution, people);

    private ManifestReadModel Read(ManifestReadModel manifest)
    {
        var (name, at) = changes.Of(manifest.Id);
        return manifest with { LastChangedByName = name, LastChangedAt = at };
    }

    private readonly Dictionary<string, ManifestReadModel> manifests = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Standing in for the allocations the SQL joins; shared with the convoy fake.</summary>
    internal BoxAllocationLedger Ledger { get; set; } = new();

    private int vehicleWeightKg;
    private string? vehiclePlate = "AB12CDE";
    private VehicleCargoCapacityReadModel vehicleCargoCapacity = new(null, null, null, null);

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

    public InMemoryManifestRepository WithVehiclePlate(string? plate)
    {
        vehiclePlate = plate;
        return this;
    }

    public InMemoryManifestRepository WithVehicleCargoCapacity(VehicleCargoCapacityReadModel capacity)
    {
        vehicleCargoCapacity = capacity;
        return this;
    }

    /// <summary>Allocates a box to the vehicle this manifest is the paperwork for.</summary>
    public InMemoryManifestRepository WithBoxOn(string manifestId, ManifestBoxReadModel box)
    {
        var manifest = manifests[manifestId];
        Ledger.KnowBox(box);
        Ledger.Allocate(
            new ConvoyVehicle { ConvoyId = new ConvoyId(manifest.ConvoyId), Vin = manifest.Vin }, box.BoxId);
        return this;
    }

    public int Count => manifests.Count;

    public ManifestReadModel? Manifest(string id) => manifests.GetValueOrDefault(id);

    public Task<ManifestReadModel?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(manifests.TryGetValue(id, out var manifest) ? Read(manifest) : null);

    public Task<IReadOnlyList<ManifestReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ManifestReadModel>>(
            manifests.Values
                .OrderBy(manifest => manifest.Id, StringComparer.Ordinal)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(Read)
                .ToList());

    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(manifests.ContainsKey(id));

    /// <summary>Mirrors <c>UQ_Manifest_ConvoyVehicle</c>: one manifest per vehicle per convoy.</summary>
    public Task<ManifestReadModel?> GetForVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken) =>
        Task.FromResult(manifests.Values
            .FirstOrDefault(manifest =>
                manifest.ConvoyId == convoyId && string.Equals(manifest.Vin, vin, StringComparison.OrdinalIgnoreCase)) is { } found
            ? Read(found)
            : null);

    public Task AddAsync(ManifestReadModel manifest, CancellationToken cancellationToken)
    {
        manifests[manifest.Id] = manifest;
        changes.Stamp(manifest.Id);
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(ManifestReadModel manifest, CancellationToken cancellationToken)
    {
        if (!manifests.TryGetValue(manifest.Id, out var existing))
        {
            return Task.FromResult(false);
        }

        // Mirrors the SQL, whose UPDATE lists only the notes and the ferry booking: the convoy and
        // the vehicle are the manifest's identity, and the status and GMR stamp belong to the
        // transitions.
        manifests[manifest.Id] = existing with
        {
            DeliveryNotes = manifest.DeliveryNotes,
        };
        changes.Stamp(manifest.Id);

        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        changes.Forget(id);
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
        changes.Stamp(id);
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
        changes.Stamp(id);

        return Task.FromResult<DateTime?>(stamped);
    }

    public Task<int> GetVehicleWeightKgAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(vehicleWeightKg);

    public Task<string?> GetVehiclePlateAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(manifests.ContainsKey(id) ? vehiclePlate : null);

    public Task<VehicleCargoCapacityReadModel> GetVehicleCargoCapacityAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(vehicleCargoCapacity);

    private IReadOnlyList<ManifestBoxReadModel> CargoOf(string id) =>
        manifests.TryGetValue(id, out var manifest) ? Ledger.BoxesOn(manifest.ConvoyId, manifest.Vin) : [];

    public Task<IReadOnlyList<ManifestDocumentLineReadModel>> GetDocumentLinesAsync(
        string id, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ManifestDocumentLineReadModel>>(
            CargoOf(id)
                .Select(box => new ManifestDocumentLineReadModel(
                    box.BoxId, box.WeightKg, ItemCount: 0, "Kharkiv Regional Hospital", "Kharkiv oblast"))
                .ToList());

    /// <summary>The one receiver this fake's boxes are all bound for.</summary>
    private static readonly Guid Receiver = new("3f1a6c20-5b4d-4e71-8a92-1c0d7e2f4b33");

    /// <summary>
    /// One goods line per box, carrying a commodity code, so a filing sheet composed from this fake
    /// reports nothing missing unless a test arranges for it to.
    /// </summary>
    /// <remarks>
    /// The SQL returns a row per <em>item</em> and repeats the box's weight across them; one item per
    /// box is the simplest shape that still exercises the grouping, and it keeps the fake's
    /// de-duplication honest — a caller that summed rows rather than distinct boxes would pass here
    /// and double-count in production.
    /// </remarks>
    public Task<IReadOnlyList<EnsGoodsLineReadModel>> GetEnsGoodsLinesAsync(
        string id, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EnsGoodsLineReadModel>>(
            CargoOf(id)
                .Select(box => new EnsGoodsLineReadModel(
                    box.BoxId, box.WeightKg, box.Validated, Receiver,
                    "Kharkiv Regional Hospital", "Kharkiv oblast",
                    $"Aid supplies in box {box.BoxId}", EnsCommodity.HumanitarianAid))
                .ToList());
}

/// <summary>
/// Captures what would have gone on the work queues, so the endpoint tests can assert that
/// approving a manifest hands off exactly one of each — and that nothing else does.
/// </summary>
internal sealed class RecordingManifestWorkQueue : IManifestWorkQueue
{
    public List<GmrSubmissionRequest> Submissions { get; } = [];

    public List<ManifestDocumentRequest> Documents { get; } = [];

    public List<EloEnvelopeRequest> Envelopes { get; } = [];

    public Task EnqueueGmrSubmissionAsync(GmrSubmissionRequest submission, CancellationToken cancellationToken)
    {
        Submissions.Add(submission);
        return Task.CompletedTask;
    }

    public Task EnqueueDocumentAsync(ManifestDocumentRequest document, CancellationToken cancellationToken)
    {
        Documents.Add(document);
        return Task.CompletedTask;
    }

    public Task EnqueueEloEnvelopeAsync(EloEnvelopeRequest envelope, CancellationToken cancellationToken)
    {
        Envelopes.Add(envelope);
        return Task.CompletedTask;
    }
}
