using UA.Action.Freedom.Application.Boxes;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Dictionary-backed box persistence so the endpoint tests run without a database.
/// </summary>
internal sealed class InMemoryBoxRepository : IBoxRepository
{
    private readonly Dictionary<int, BoxReadModel> boxes = [];
    private readonly Dictionary<int, List<BoxItemReadModel>> items = [];
    private readonly List<BoxQrCodeReadModel> qrCodes = [];

    private int nextId = 1;

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

    public Task<BoxReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(boxes.GetValueOrDefault(id));

    public Task<IReadOnlyList<BoxReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BoxReadModel>>(
            boxes.Values.OrderBy(box => box.Id).Skip((page - 1) * pageSize).Take(pageSize).ToList());

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(boxes.ContainsKey(id));

    public Task<int> AddAsync(BoxReadModel box, CancellationToken cancellationToken)
    {
        var id = nextId++;
        boxes[id] = box with { Id = id, WeightKg = 0, ValidatedByPersonId = null, ValidatedAt = null };
        return Task.FromResult(id);
    }

    public Task<bool> UpdateAsync(BoxReadModel box, CancellationToken cancellationToken)
    {
        if (!boxes.TryGetValue(box.Id, out var existing))
        {
            return Task.FromResult(false);
        }

        // Mirrors the SQL, which cannot touch weight or the validation record on an update.
        boxes[box.Id] = box with
        {
            WeightKg = existing.WeightKg,
            ValidatedByPersonId = existing.ValidatedByPersonId,
            ValidatedAt = existing.ValidatedAt,
        };

        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        items.Remove(id);
        return Task.FromResult(boxes.Remove(id));
    }

    public Task<bool> ValidateAsync(
        int id, Guid validatedByPersonId, int weightKg, DateTime validatedAt, CancellationToken cancellationToken)
    {
        if (!boxes.TryGetValue(id, out var box) || box.Validated)
        {
            return Task.FromResult(false);
        }

        boxes[id] = box with
        {
            WeightKg = weightKg,
            ValidatedByPersonId = validatedByPersonId,
            ValidatedAt = validatedAt,
        };

        return Task.FromResult(true);
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
}
