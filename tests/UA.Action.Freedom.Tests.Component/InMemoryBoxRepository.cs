using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Dictionary-backed box persistence so the endpoint tests run without a database.
/// </summary>
internal sealed class InMemoryBoxRepository : IBoxRepository, IRecordsWhoChanged
{
    private readonly ChangeLedger<int> changes = new();

    public void Attach(IChangeAttribution attribution, IPersonRepository people) =>
        changes.Attach(attribution, people);

    private BoxReadModel Read(BoxReadModel box)
    {
        var (name, at) = changes.Of(box.Id);
        var replacement = boxes.Values.FirstOrDefault(other => other.ReplacesBoxId == box.Id);
        return box with { LastChangedByName = name, LastChangedAt = at, ReplacedByBoxId = replacement?.Id };
    }

    private readonly Dictionary<int, BoxReadModel> boxes = [];
    private readonly Dictionary<int, List<BoxItemReadModel>> items = [];
    private readonly List<BoxQrCodeReadModel> qrCodes = [];
    private readonly List<BoxBayAssignmentReadModel> bayAssignments = [];

    private int nextId = 1;
    private int nextBayAssignmentId = 1;

    public InMemoryBoxRepository(params BoxReadModel[] seed)
    {
        foreach (var box in seed)
        {
            boxes[box.Id] = box;
            nextId = Math.Max(nextId, box.Id + 1);
        }
    }

    public int Count => boxes.Count;

    public BoxReadModel? Box(int id) => boxes.GetValueOrDefault(id);

    public IReadOnlyList<BoxItemReadModel> Items(int boxId) => items.GetValueOrDefault(boxId, []);

    public BoxQrCodeReadModel? ActiveQrCode(int boxId) =>
        qrCodes.SingleOrDefault(code => code.BoxId == boxId && code.Active);

    public InMemoryBoxRepository WithItem(int boxId, BoxItemReadModel item)
    {
        items.TryAdd(boxId, []);
        items[boxId].Add(item);
        return this;
    }

    public InMemoryBoxRepository WithQrCode(BoxQrCodeReadModel code)
    {
        qrCodes.Add(code);
        return this;
    }

    public InMemoryBoxRepository WithBayAssignment(BoxBayAssignmentReadModel assignment)
    {
        bayAssignments.Add(assignment);
        nextBayAssignmentId = Math.Max(nextBayAssignmentId, assignment.Id + 1);
        return this;
    }

    public Task<BoxReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(boxes.TryGetValue(id, out var box) ? Read(box) : null);

    public Task<IReadOnlyList<BoxReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BoxReadModel>>(
            boxes.Values.OrderBy(box => box.Id).Skip((page - 1) * pageSize).Take(pageSize).Select(Read).ToList());

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(boxes.ContainsKey(id));

    public Task<int> AddAsync(BoxReadModel box, CancellationToken cancellationToken)
    {
        var id = nextId++;
        boxes[id] = box with
        {
            Id = id, WeightKg = 0, WidthCm = null, DepthCm = null, HeightCm = null,
            ValidatedByPersonId = null, ValidatedAt = null,
        };
        changes.Stamp(id);
        return Task.FromResult(id);
    }

    public Task<bool> UpdateAsync(BoxReadModel box, CancellationToken cancellationToken)
    {
        if (!boxes.TryGetValue(box.Id, out var existing))
        {
            return Task.FromResult(false);
        }

        // Mirrors the SQL, which cannot touch weight, dimensions or the validation record on an update.
        boxes[box.Id] = box with
        {
            WeightKg = existing.WeightKg,
            WidthCm = existing.WidthCm,
            DepthCm = existing.DepthCm,
            HeightCm = existing.HeightCm,
            ValidatedByPersonId = existing.ValidatedByPersonId,
            ValidatedAt = existing.ValidatedAt,
        };
        changes.Stamp(box.Id);

        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        items.Remove(id);
        changes.Forget(id);
        return Task.FromResult(boxes.Remove(id));
    }

    public Task<bool> ValidateAsync(
        int id, Guid validatedByPersonId, int weightKg,
        decimal? widthCm, decimal? depthCm, decimal? heightCm,
        DateTime validatedAt, CancellationToken cancellationToken)
    {
        if (!boxes.TryGetValue(id, out var box) || box.Validated)
        {
            return Task.FromResult(false);
        }

        boxes[id] = box with
        {
            WeightKg = weightKg,
            WidthCm = widthCm,
            DepthCm = depthCm,
            HeightCm = heightCm,
            ValidatedByPersonId = validatedByPersonId,
            ValidatedAt = validatedAt,
        };
        changes.Stamp(id);

        return Task.FromResult(true);
    }

    /// <summary>The allocation ledger the convoy fake shares, so replacing a box moves its cargo as the SQL does.</summary>
    internal BoxAllocationLedger? Cargo { get; set; }

    public Task<int?> ReplaceAsync(int boxId, CancellationToken cancellationToken)
    {
        // Mirrors the SQL: conditional on the box being attested and not already voided.
        if (!boxes.TryGetValue(boxId, out var box) || !box.Validated || box.Voided)
        {
            return Task.FromResult<int?>(null);
        }

        var replacementId = nextId++;
        boxes[boxId] = box with { VoidedAt = DateTime.UtcNow };
        changes.Stamp(boxId);

        // The old label stops resolving at once; revoked rows are kept.
        for (var i = 0; i < qrCodes.Count; i++)
        {
            if (qrCodes[i].BoxId == boxId && qrCodes[i].Active)
            {
                qrCodes[i] = qrCodes[i] with { RevokedAt = DateTime.UtcNow };
            }
        }

        boxes[replacementId] = box with
        {
            Id = replacementId, WeightKg = 0, WidthCm = null, DepthCm = null, HeightCm = null,
            ValidatedByPersonId = null, ValidatedAt = null, VoidedAt = null, ReplacesBoxId = boxId,
        };
        changes.Stamp(replacementId);

        // The same donation described again: every item copies, with a new identity.
        items[replacementId] = [.. items.GetValueOrDefault(boxId, []).Select(item => item with { Id = Guid.NewGuid() })];

        for (var i = 0; i < bayAssignments.Count; i++)
        {
            if (bayAssignments[i].BoxId == boxId && bayAssignments[i].Active)
            {
                bayAssignments[i] = bayAssignments[i] with { VacatedAt = DateTime.UtcNow };
            }
        }

        Cargo?.Replace(boxId, new ManifestBoxReadModel(replacementId, 0, Validated: false));

        return Task.FromResult<int?>(replacementId);
    }

    public Task<IReadOnlyList<BoxItemReadModel>> ListItemsAsync(int boxId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BoxItemReadModel>>(items.GetValueOrDefault(boxId, []));

    public Task AddItemAsync(int boxId, BoxItemReadModel item, CancellationToken cancellationToken)
    {
        items.TryAdd(boxId, []);
        items[boxId].Add(item);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteItemAsync(int boxId, Guid itemId, CancellationToken cancellationToken)
    {
        if (!items.TryGetValue(boxId, out var packed))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(packed.RemoveAll(item => item.Id == itemId) > 0);
    }

    public Task<BoxQrCodeReadModel?> GetActiveQrCodeAsync(int boxId, CancellationToken cancellationToken) =>
        Task.FromResult(qrCodes.SingleOrDefault(code => code.BoxId == boxId && code.Active));

    public Task<BoxQrCodeReadModel?> ResolveActiveQrCodeAsync(Guid token, CancellationToken cancellationToken) =>
        Task.FromResult(qrCodes.SingleOrDefault(code => code.Token == token && code.Active));

    public Task<BoxQrCodeReadModel> IssueQrCodeAsync(
        int boxId, Guid token, DateTime issuedAt, CancellationToken cancellationToken)
    {
        // Mirrors the SQL transaction: any live code for the box is revoked before the new one
        // is added, so exactly one stays active.
        for (var i = 0; i < qrCodes.Count; i++)
        {
            if (qrCodes[i].BoxId == boxId && qrCodes[i].Active)
            {
                qrCodes[i] = qrCodes[i] with { RevokedAt = issuedAt };
            }
        }

        var code = new BoxQrCodeReadModel(token, boxId, issuedAt, RevokedAt: null);
        qrCodes.Add(code);
        return Task.FromResult(code);
    }

    public Task<bool> RevokeActiveQrCodeAsync(int boxId, CancellationToken cancellationToken)
    {
        var revoked = false;

        for (var i = 0; i < qrCodes.Count; i++)
        {
            if (qrCodes[i].BoxId == boxId && qrCodes[i].Active)
            {
                qrCodes[i] = qrCodes[i] with { RevokedAt = DateTime.UtcNow };
                revoked = true;
            }
        }

        return Task.FromResult(revoked);
    }

    public Task<BoxBayAssignmentReadModel?> GetActiveBayAssignmentAsync(int boxId, CancellationToken cancellationToken) =>
        Task.FromResult(bayAssignments.SingleOrDefault(assignment => assignment.BoxId == boxId && assignment.Active));

    public Task<IReadOnlyList<BoxBayAssignmentReadModel>> ListBayAssignmentHistoryAsync(int boxId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BoxBayAssignmentReadModel>>(
            bayAssignments.Where(assignment => assignment.BoxId == boxId)
                .OrderByDescending(assignment => assignment.AssignedAt)
                .ThenByDescending(assignment => assignment.Id)
                .ToList());

    public Task<BoxBayAssignmentReadModel> AssignBayAsync(
        int boxId, int bayId, Guid assignedByPersonId, DateTime assignedAt, CancellationToken cancellationToken)
    {
        // Mirrors the SQL transaction: any live assignment for the box is vacated before the new
        // one is added, so exactly one stays active.
        for (var i = 0; i < bayAssignments.Count; i++)
        {
            if (bayAssignments[i].BoxId == boxId && bayAssignments[i].Active)
            {
                bayAssignments[i] = bayAssignments[i] with { VacatedAt = assignedAt };
            }
        }

        var assignment = new BoxBayAssignmentReadModel(
            nextBayAssignmentId++, boxId, bayId, assignedByPersonId, assignedAt, VacatedAt: null);
        bayAssignments.Add(assignment);
        return Task.FromResult(assignment);
    }

    public Task<bool> VacateActiveBayAssignmentAsync(int boxId, CancellationToken cancellationToken)
    {
        var vacated = false;

        for (var i = 0; i < bayAssignments.Count; i++)
        {
            if (bayAssignments[i].BoxId == boxId && bayAssignments[i].Active)
            {
                bayAssignments[i] = bayAssignments[i] with { VacatedAt = DateTime.UtcNow };
                vacated = true;
            }
        }

        return Task.FromResult(vacated);
    }
}
