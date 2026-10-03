using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Donations;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// One fake behind both donor and donation ports, as the split in production is two tables rather than two
/// stores. It enforces what the SQL does: erasing a donor deletes their details and removes the identity only when
/// no donation names it, a donation that items still name cannot be deleted, and an erased donor reads
/// "Former donor" wherever a donation shows them.
/// </summary>
internal sealed class InMemoryDonationRepository : IDonorRepository, IDonationRepository, IRecordsWhoChanged
{
    private sealed record Row(Guid DonorId, DateOnly ReceivedOn, string? Notes);

    private readonly Dictionary<Guid, DonorReadModel> donors = [];

    private readonly HashSet<Guid> erasedButNamed = [];

    private readonly Dictionary<int, Row> donations = [];

    private readonly HashSet<int> namedByItems = [];

    private readonly List<(Guid DonorId, DonorReportItem Item)> reportItems = [];

    private readonly ChangeLedger<Guid> donorChanges = new();

    private readonly ChangeLedger<int> donationChanges = new();

    private int nextDonationId = 100;

    public void Attach(IChangeAttribution attribution, IPersonRepository people)
    {
        donorChanges.Attach(attribution, people);
        donationChanges.Attach(attribution, people);
    }

    public InMemoryDonationRepository WithDonor(DonorReadModel donor)
    {
        donors[donor.Id] = donor;
        return this;
    }

    public InMemoryDonationRepository WithDonation(int id, Guid donorId, DateOnly receivedOn, string? notes = null)
    {
        donations[id] = new Row(donorId, receivedOn, notes);
        return this;
    }

    /// <summary>Items in a box still name this donation, so it cannot be deleted.</summary>
    public InMemoryDonationRepository WithItemsIn(int donationId)
    {
        namedByItems.Add(donationId);
        return this;
    }

    public InMemoryDonationRepository WithReportItem(Guid donorId, DonorReportItem item)
    {
        reportItems.Add((donorId, item));
        return this;
    }

    public bool HasIdentity(Guid donorId) => donors.ContainsKey(donorId) || erasedButNamed.Contains(donorId);

    public bool HasDetail(Guid donorId) => donors.ContainsKey(donorId);

    public bool HasDonation(int id) => donations.ContainsKey(id);

    private DonorReadModel Read(DonorReadModel donor)
    {
        var (name, at) = donorChanges.Of(donor.Id);
        return donor with { LastChangedByName = name, LastChangedAt = at };
    }

    private string? DisplayName(Guid donorId) =>
        donors.TryGetValue(donorId, out var donor) ? donor.Name
        : erasedButNamed.Contains(donorId) ? DonorNames.FormerDonor
        : null;

    private DonationReadModel Read(int id, Row row)
    {
        var (name, at) = donationChanges.Of(id);
        return new DonationReadModel(id, row.DonorId, DisplayName(row.DonorId) ?? DonorNames.FormerDonor, row.ReceivedOn, row.Notes, name, at);
    }

    public Task<DonorReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<DonorReadModel?>(donors.TryGetValue(id, out var donor) ? Read(donor) : null);

    public Task<IReadOnlyList<DonorReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DonorReadModel>>(
            donors.Values
                .OrderBy(donor => donor.Name, StringComparer.Ordinal)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(Read)
                .ToList());

    public Task AddAsync(DonorReadModel donor, CancellationToken cancellationToken)
    {
        donors[donor.Id] = donor;
        donorChanges.Stamp(donor.Id);
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(DonorReadModel donor, CancellationToken cancellationToken)
    {
        if (!donors.ContainsKey(donor.Id))
        {
            return Task.FromResult(false);
        }

        donors[donor.Id] = donor;
        donorChanges.Stamp(donor.Id);
        return Task.FromResult(true);
    }

    public Task<bool> EraseAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!donors.Remove(id))
        {
            return Task.FromResult(false);
        }

        if (donations.Values.Any(row => row.DonorId == id))
        {
            erasedButNamed.Add(id);
        }

        return Task.FromResult(true);
    }

    Task<DonationReadModel?> IDonationRepository.GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult<DonationReadModel?>(donations.TryGetValue(id, out var row) ? Read(id, row) : null);

    public Task<IReadOnlyList<DonationReadModel>> ListAsync(
        Guid? donorId, int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DonationReadModel>>(
            donations
                .Where(entry => donorId is null || entry.Value.DonorId == donorId)
                .OrderByDescending(entry => entry.Value.ReceivedOn)
                .ThenByDescending(entry => entry.Key)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(entry => Read(entry.Key, entry.Value))
                .ToList());

    public Task<int> AddAsync(Guid donorId, DateOnly receivedOn, string? notes, CancellationToken cancellationToken)
    {
        var id = ++nextDonationId;
        donations[id] = new Row(donorId, receivedOn, notes);
        donationChanges.Stamp(id);
        return Task.FromResult(id);
    }

    public Task<bool> UpdateAsync(int id, DateOnly receivedOn, string? notes, CancellationToken cancellationToken)
    {
        if (!donations.TryGetValue(id, out var row))
        {
            return Task.FromResult(false);
        }

        donations[id] = row with { ReceivedOn = receivedOn, Notes = notes };
        donationChanges.Stamp(id);
        return Task.FromResult(true);
    }

    public Task<DeleteDonationResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        if (!donations.ContainsKey(id))
        {
            return Task.FromResult(DeleteDonationResult.NotFound);
        }

        if (namedByItems.Contains(id))
        {
            return Task.FromResult(DeleteDonationResult.StillReferenced);
        }

        donations.Remove(id);
        donationChanges.Forget(id);
        return Task.FromResult(DeleteDonationResult.Deleted);
    }

    public Task<string?> DonorNameAsync(Guid donorId, CancellationToken cancellationToken) =>
        Task.FromResult(DisplayName(donorId));

    public Task<IReadOnlyList<DonorReportItem>> ReportItemsAsync(Guid donorId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DonorReportItem>>(
            reportItems.Where(entry => entry.DonorId == donorId).Select(entry => entry.Item).ToList());
}
