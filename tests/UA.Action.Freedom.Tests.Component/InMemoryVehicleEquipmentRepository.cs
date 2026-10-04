using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// In-memory stand-in for <c>VehicleEquipmentRepository</c>. It mirrors the SQL: catalogue names are unique
/// (case-insensitively, as the collation makes them), replacing a vehicle's equipment needs the vehicle to be on the
/// convoy and every item to be in the catalogue and changes nothing otherwise, and equipment goes with a truck-list
/// entry that has been removed (the CASCADE), which is read from the convoy fake so the two cannot disagree.
/// </summary>
internal sealed class InMemoryVehicleEquipmentRepository(InMemoryConvoyRepository? convoys = null)
    : IVehicleEquipmentRepository, IRecordsWhoChanged
{
    private readonly ChangeLedger<string> changes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(int Id, string Name, decimal? UnitCostGbp)> items = [];
    private readonly Dictionary<(int ConvoyId, string Vin, int ItemId), (int Quantity, decimal? CostGbp)> lines = [];
    private int nextItemId = 1;

    public void Attach(IChangeAttribution attribution, IPersonRepository people) => changes.Attach(attribution, people);

    private static string ItemKey(int id) => $"item/{id}";

    private static string LineKey(int convoyId, string vin, int itemId) => $"line/{convoyId}/{vin}/{itemId}";

    private bool OnConvoy(int convoyId, string vin) =>
        convoys?.GetAsync(convoyId, vin, CancellationToken.None).GetAwaiter().GetResult() is not null;

    public Task<IReadOnlyList<EquipmentItemReadModel>> ListItemsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<EquipmentItemReadModel> result = items
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item =>
            {
                var (name, at) = changes.Of(ItemKey(item.Id));
                return new EquipmentItemReadModel(item.Id, item.Name, item.UnitCostGbp, name, at);
            })
            .ToList();

        return Task.FromResult(result);
    }

    public Task<int?> AddItemAsync(string name, decimal? unitCostGbp, CancellationToken cancellationToken)
    {
        if (items.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult<int?>(null);
        }

        var id = nextItemId++;
        items.Add((id, name, unitCostGbp));
        changes.Stamp(ItemKey(id));
        return Task.FromResult<int?>(id);
    }

    private IEnumerable<VehicleEquipmentReadModel> Read(Func<(int ConvoyId, string Vin, int ItemId), bool> filter) =>
        lines
            .Where(line => filter(line.Key) && OnConvoy(line.Key.ConvoyId, line.Key.Vin))
            .OrderBy(line => line.Key.Vin, StringComparer.OrdinalIgnoreCase)
            .Select(line =>
            {
                var item = items.Single(candidate => candidate.Id == line.Key.ItemId);
                var (name, at) = changes.Of(LineKey(line.Key.ConvoyId, line.Key.Vin, line.Key.ItemId));
                return new VehicleEquipmentReadModel(
                    line.Key.Vin, item.Id, item.Name, line.Value.Quantity, item.UnitCostGbp, line.Value.CostGbp, name, at);
            })
            .OrderBy(line => line.Vin, StringComparer.OrdinalIgnoreCase)
            .ThenBy(line => line.Name, StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<VehicleEquipmentReadModel>> ListForVehicleAsync(
        int convoyId, string vin, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VehicleEquipmentReadModel>>(
            Read(key => key.ConvoyId == convoyId && string.Equals(key.Vin, vin, StringComparison.OrdinalIgnoreCase)).ToList());

    public Task<IReadOnlyList<VehicleEquipmentReadModel>> ListForConvoyAsync(int convoyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VehicleEquipmentReadModel>>(Read(key => key.ConvoyId == convoyId).ToList());

    public Task<ReplaceEquipmentResult> ReplaceForVehicleAsync(
        int convoyId, string vin, IReadOnlyList<VehicleEquipmentLine> replacement, CancellationToken cancellationToken)
    {
        if (!OnConvoy(convoyId, vin))
        {
            return Task.FromResult(ReplaceEquipmentResult.VehicleNotOnConvoy);
        }

        if (replacement.Any(line => items.All(item => item.Id != line.EquipmentItemId)))
        {
            return Task.FromResult(ReplaceEquipmentResult.UnknownItem);
        }

        var key = vin.ToUpperInvariant();
        foreach (var existing in lines.Keys.Where(k => k.ConvoyId == convoyId && k.Vin == key).ToList())
        {
            lines.Remove(existing);
            changes.Forget(LineKey(convoyId, key, existing.ItemId));
        }

        foreach (var line in replacement)
        {
            lines.Add((convoyId, key, line.EquipmentItemId), (line.Quantity, line.CostGbp));
            changes.Stamp(LineKey(convoyId, key, line.EquipmentItemId));
        }

        return Task.FromResult(ReplaceEquipmentResult.Replaced);
    }
}
