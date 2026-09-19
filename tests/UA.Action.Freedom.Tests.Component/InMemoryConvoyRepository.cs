using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// A dictionary-backed <see cref="IConvoyRepository"/> so the endpoint tests run without a
/// database. The Dapper implementation is covered separately by the integration tests.
/// </summary>
/// <remarks>
/// Every rule here mirrors a statement in <c>ConvoyRepository</c>: a crew seat names its convoy,
/// a person holds one seat per convoy (<c>UQ_VehicleDriver_Convoy_Person</c>), and seats are
/// cleared when the vehicle leaves its convoy or the convoy is cancelled. A fake that is kinder than the SQL lets a test pass that production fails.
/// </remarks>
internal sealed class InMemoryConvoyRepository : IConvoyRepository
{
    private sealed record StoredVehicle(int? ConvoyId, InspectionStatus Inspection, bool HandedOver = false);

    /// <summary>
    /// The status of each vehicle's manifest on its convoy — standing in for the join to
    /// <c>dbo.Manifest</c> that arrival makes.
    /// </summary>
    private readonly Dictionary<string, ManifestStatus> manifestStatus = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<int, ConvoyReadModel> convoys = [];
    private readonly Dictionary<int, List<RouteStopReadModel>> routes = [];

    /// <summary>Standing in for <c>dbo.Vehicle.ConvoyId</c> and <c>InspectionStatus</c>.</summary>
    private readonly Dictionary<string, StoredVehicle> vehicles = new(StringComparer.OrdinalIgnoreCase);

    private sealed record CrewSeat(int ConvoyId, string Vin, Guid PersonId, CrewRole Role);

    /// <summary>
    /// Standing in for <c>dbo.VehicleDriver</c>, keyed as the table is: one seat per person per
    /// convoy (<c>UQ_VehicleDriver_Convoy_Person</c>).
    /// </summary>
    private readonly List<CrewSeat> crew = [];

    /// <summary>Standing in for <c>dbo.VehicleInsurance</c>, keyed (ConvoyId, Vin).</summary>
    private readonly Dictionary<(int ConvoyId, string Vin), VehicleInsuranceReadModel> insurance = [];

    /// <summary>Names for the crew list, standing in for the join to <c>dbo.Person</c>; the role comes from the seat.</summary>
    private readonly Dictionary<Guid, VehicleDriverReadModel> persons = [];

    private int nextId = 1;

    public InMemoryConvoyRepository(params ConvoyReadModel[] seed)
    {
        foreach (var convoy in seed)
        {
            convoys[convoy.Id] = convoy;
            nextId = Math.Max(nextId, convoy.Id + 1);
        }
    }

    /// <summary>
    /// Adds a vehicle. It has passed its inspection unless told otherwise, because that is the
    /// only kind a convoy will take and most tests are not about the inspection.
    /// </summary>
    public InMemoryConvoyRepository WithVehicle(
        string vin, int? onConvoy = null, InspectionStatus inspection = InspectionStatus.Passed)
    {
        vehicles[vin] = new StoredVehicle(onConvoy, inspection);
        return this;
    }

    public InMemoryConvoyRepository WithPerson(Guid personId, string firstName, string lastName)
    {
        persons[personId] = new VehicleDriverReadModel(personId, firstName, lastName, CrewRole.Driver);
        return this;
    }

    public InMemoryConvoyRepository WithDriver(string vin, Guid personId, CrewRole role = CrewRole.Driver)
    {
        crew.Add(new CrewSeat(ConvoyOf(vin) ?? throw new InvalidOperationException($"{vin} is on no convoy."), vin, personId, role));
        return this;
    }

    public InMemoryConvoyRepository WithManifest(string vin, ManifestStatus status)
    {
        manifestStatus[vin] = status;
        return this;
    }

    public InMemoryConvoyRepository WithHandedOverVehicle(string vin)
    {
        vehicles[vin] = new StoredVehicle(null, InspectionStatus.Passed, HandedOver: true);
        return this;
    }

    public bool IsHandedOver(string vin) => vehicles.GetValueOrDefault(vin)?.HandedOver ?? false;

    public InMemoryConvoyRepository WithInsurance(VehicleInsuranceReadModel policy)
    {
        insurance[(policy.ConvoyId, policy.Vin.ToUpperInvariant())] = policy;
        return this;
    }

    public VehicleInsuranceReadModel? InsuranceOf(int convoyId, string vin) =>
        insurance.GetValueOrDefault((convoyId, vin.ToUpperInvariant()));

    public int Count => convoys.Count;

    public int? ConvoyOf(string vin) => vehicles.GetValueOrDefault(vin)?.ConvoyId;

    public IReadOnlyList<Guid> DriverIdsOf(string vin) =>
        crew.Where(seat => seat.ConvoyId == ConvoyOf(vin) && Same(seat.Vin, vin)).Select(seat => seat.PersonId).ToList();

    public IReadOnlyList<RouteStopReadModel> RouteOf(int convoyId) =>
        routes.GetValueOrDefault(convoyId, []);

    public Task<ConvoyReadModel?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(convoys.GetValueOrDefault(id));

    public Task<IReadOnlyList<ConvoyReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConvoyReadModel>>(
            convoys.Values
                .OrderByDescending(convoy => convoy.Start)
                .ThenByDescending(convoy => convoy.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList());

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(convoys.ContainsKey(id));

    public Task<int> AddAsync(DateTime start, DateTime expectedEnd, CancellationToken cancellationToken)
    {
        var id = nextId++;
        convoys[id] = new ConvoyReadModel(id, start, expectedEnd, TruckListPublishedAt: null);
        return Task.FromResult(id);
    }

    public Task<bool> UpdateAsync(ConvoyReadModel convoy, CancellationToken cancellationToken)
    {
        if (!convoys.TryGetValue(convoy.Id, out var existing))
        {
            return Task.FromResult(false);
        }

        // Mirrors the SQL, which does not touch TruckListPublishedAt on an ordinary update.
        convoys[convoy.Id] = convoy with { TruckListPublishedAt = existing.TruckListPublishedAt };
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        routes.Remove(id);
        crew.RemoveAll(seat => seat.ConvoyId == id);
        foreach (var key in insurance.Keys.Where(key => key.ConvoyId == id).ToList())
        {
            insurance.Remove(key);
        }

        foreach (var vin in VinsOn(id))
        {
            Release(vin);
        }

        return Task.FromResult(convoys.Remove(id));
    }

    public Task<IReadOnlyList<RouteStopReadModel>> GetRouteAsync(int convoyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RouteStopReadModel>>(routes.GetValueOrDefault(convoyId, []));

    public Task ReplaceRouteAsync(
        int convoyId, IReadOnlyList<RouteStopReadModel> stops, CancellationToken cancellationToken)
    {
        routes[convoyId] = [.. stops];
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ConvoyVehicleReadModel>> ListVehiclesAsync(int convoyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConvoyVehicleReadModel>>(
            VinsOn(convoyId)
                .Order(StringComparer.Ordinal)
                .Select(vin => new ConvoyVehicleReadModel(
                    vin, "AB12CDE", 1_400, CountOf(convoyId, vin, CrewRole.Driver), CountOf(convoyId, vin, CrewRole.Passenger)))
                .ToList());

    public Task<AssignVehicleResult> AssignVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        if (!vehicles.TryGetValue(vin, out var vehicle))
        {
            return Task.FromResult(AssignVehicleResult.VehicleNotFound);
        }

        if (vehicle.HandedOver)
        {
            return Task.FromResult(AssignVehicleResult.HandedOver);
        }

        if (vehicle.Inspection != InspectionStatus.Passed)
        {
            return Task.FromResult(AssignVehicleResult.NotPassedInspection);
        }

        if (vehicle.ConvoyId is not null && vehicle.ConvoyId != convoyId)
        {
            return Task.FromResult(AssignVehicleResult.OnAnotherConvoy);
        }

        vehicles[vin] = vehicle with { ConvoyId = convoyId };
        return Task.FromResult(AssignVehicleResult.Assigned);
    }

    public Task<bool> UnassignVehicleAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        if (!IsOn(convoyId, vin))
        {
            return Task.FromResult(false);
        }

        Release(vin);
        return Task.FromResult(true);
    }

    public Task<bool> PublishTruckListAsync(int convoyId, DateTime publishedAt, CancellationToken cancellationToken)
    {
        if (!convoys.TryGetValue(convoyId, out var convoy) || convoy.TruckListPublished)
        {
            return Task.FromResult(false);
        }

        convoys[convoyId] = convoy with { TruckListPublishedAt = publishedAt };
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<VehicleDriverReadModel>?> ListVehicleDriversAsync(
        int convoyId, string vin, CancellationToken cancellationToken)
    {
        if (!IsOn(convoyId, vin))
        {
            return Task.FromResult<IReadOnlyList<VehicleDriverReadModel>?>(null);
        }

        var drivers = crew
            .Where(seat => seat.ConvoyId == convoyId && Same(seat.Vin, vin))
            .Select(seat => persons[seat.PersonId] with { Role = seat.Role })
            .OrderBy(member => member.Role)
            .ThenBy(member => member.LastName)
            .ThenBy(member => member.FirstName)
            .ToList();

        return Task.FromResult<IReadOnlyList<VehicleDriverReadModel>?>(drivers);
    }

    public Task<AssignDriverResult> AssignDriverAsync(
        int convoyId, string vin, Guid personId, CrewRole role, CancellationToken cancellationToken)
    {
        var seat = crew.Find(existing => existing.ConvoyId == convoyId && existing.PersonId == personId);
        if (seat is not null)
        {
            return Task.FromResult(Same(seat.Vin, vin) ? AssignDriverResult.AlreadyAssigned : AssignDriverResult.OnAnotherVehicle);
        }

        if (!IsOn(convoyId, vin))
        {
            return Task.FromResult(AssignDriverResult.VehicleNotOnConvoy);
        }

        crew.Add(new CrewSeat(convoyId, vin, personId, role));
        VoidInsurance(convoyId, vin);
        return Task.FromResult(AssignDriverResult.Assigned);
    }

    public Task<bool> UnassignDriverAsync(int convoyId, string vin, Guid personId, CancellationToken cancellationToken)
    {
        var removed = IsOn(convoyId, vin)
            && crew.RemoveAll(seat => seat.ConvoyId == convoyId && Same(seat.Vin, vin) && seat.PersonId == personId) > 0;
        if (removed)
        {
            VoidInsurance(convoyId, vin);
        }

        return Task.FromResult(removed);
    }

    public Task<ArriveResult> ArriveAsync(int convoyId, DateTime arrivedAt, CancellationToken cancellationToken)
    {
        if (StillTravelling(convoyId).Count > 0)
        {
            return Task.FromResult(ArriveResult.VehiclesStillTravelling);
        }

        if (!convoys.TryGetValue(convoyId, out var convoy) || convoy.Arrived || !convoy.TruckListPublished)
        {
            return Task.FromResult(ArriveResult.AlreadyArrived);
        }

        convoys[convoyId] = convoy with { ArrivedAt = arrivedAt };
        foreach (var vin in VinsOn(convoyId))
        {
            vehicles[vin] = manifestStatus[vin] == ManifestStatus.Returned
                ? vehicles[vin] with { ConvoyId = null }
                : vehicles[vin] with { HandedOver = true };
        }

        return Task.FromResult(ArriveResult.Arrived);
    }

    public Task<IReadOnlyList<string>> ListVehiclesStillTravellingAsync(int convoyId, CancellationToken cancellationToken) =>
        Task.FromResult(StillTravelling(convoyId));

    private IReadOnlyList<string> StillTravelling(int convoyId) =>
        VinsOn(convoyId)
            .Where(vin => manifestStatus.GetValueOrDefault(vin) is not (ManifestStatus.Delivered or ManifestStatus.Lost or ManifestStatus.Returned))
            .Order(StringComparer.Ordinal)
            .ToList();

    public Task<VehicleInsuranceReadModel?> GetInsuranceAsync(int convoyId, string vin, CancellationToken cancellationToken) =>
        Task.FromResult(InsuranceOf(convoyId, vin));

    public Task<bool> RecordInsuranceAsync(VehicleInsuranceRecord policy, CancellationToken cancellationToken)
    {
        if (!IsOn(policy.ConvoyId, policy.Vin))
        {
            return Task.FromResult(false);
        }

        insurance[(policy.ConvoyId, policy.Vin.ToUpperInvariant())] = new VehicleInsuranceReadModel(
            policy.ConvoyId, policy.Vin, policy.Insurer, policy.PolicyNumber, policy.CoverStart, policy.CoverEnd,
            policy.CostGbp, policy.RecordedBy, DateTime.UtcNow, VoidedAt: null);
        return Task.FromResult(true);
    }

    public Task<bool> RemoveInsuranceAsync(int convoyId, string vin, CancellationToken cancellationToken) =>
        Task.FromResult(insurance.Remove((convoyId, vin.ToUpperInvariant())));

    private void VoidInsurance(int convoyId, string vin)
    {
        if (InsuranceOf(convoyId, vin) is { VoidedAt: null } policy)
        {
            insurance[(convoyId, vin.ToUpperInvariant())] = policy with { VoidedAt = DateTime.UtcNow };
        }
    }

    private bool IsOn(int convoyId, string vin) => ConvoyOf(vin) == convoyId;

    private List<string> VinsOn(int convoyId) =>
        vehicles.Where(entry => entry.Value.ConvoyId == convoyId).Select(entry => entry.Key).ToList();

    private int CountOf(int convoyId, string vin, CrewRole role) =>
        crew.Count(seat => seat.ConvoyId == convoyId && Same(seat.Vin, vin) && seat.Role == role);

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private void Release(string vin)
    {
        var convoyId = ConvoyOf(vin);
        crew.RemoveAll(seat => seat.ConvoyId == convoyId && Same(seat.Vin, vin));
        if (convoyId is { } id)
        {
            insurance.Remove((id, vin.ToUpperInvariant()));
        }

        vehicles[vin] = vehicles[vin] with { ConvoyId = null };
    }
}
