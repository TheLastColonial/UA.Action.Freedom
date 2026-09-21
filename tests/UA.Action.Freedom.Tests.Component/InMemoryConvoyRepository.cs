using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// A dictionary-backed stand-in for both convoy ports so the endpoint tests run without a
/// database. The Dapper implementations are covered separately by the integration tests.
/// </summary>
/// <remarks>
/// It implements <see cref="IConvoyRepository"/> and <see cref="IConvoyVehicleRepository"/>
/// together because they share one store — the truck list — and splitting the fake in two would
/// invent an indirection the production split does not need (there the boundary is two tables).
///
/// <para>
/// <strong>Every rule here mirrors a statement in the SQL.</strong> The truck list keeps withdrawn
/// vehicles; a person holds one seat per leg (<c>UQ_ConvoyVehicleCrew_Convoy_Person_Leg</c>); crew
/// and insurance are cleared when a vehicle is removed before publication and kept when it is
/// withdrawn after; a crew change voids the insurance; arrival ignores withdrawn vehicles and
/// releases nothing. A fake that is kinder than the SQL lets a test pass that production fails —
/// this one returned <c>[]</c> where the SQL returned <c>null</c> once already.
/// </para>
/// </remarks>
internal sealed class InMemoryConvoyRepository : IConvoyRepository, IConvoyVehicleRepository
{
    private sealed record StoredVehicle(InspectionStatus Inspection, bool HandedOver = false);

    /// <summary>
    /// A truck-list entry, standing in for <c>dbo.ConvoyVehicle</c>. Withdrawal is a stamp, so the
    /// row outlives the vehicle leaving the convoy.
    /// </summary>
    private sealed record TruckListEntry(
        int ConvoyId, string Vin, DateTime AddedAt, DateTime? WithdrawnAt = null, string? WithdrawnReason = null);

    /// <summary>
    /// The status of each vehicle's manifest on its convoy — standing in for the join to
    /// <c>dbo.Manifest</c> that arrival makes.
    /// </summary>
    private readonly Dictionary<string, ManifestStatus> manifestStatus = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<int, ConvoyReadModel> convoys = [];
    private readonly Dictionary<int, List<RouteStopReadModel>> routes = [];

    /// <summary>Standing in for <c>dbo.Vehicle</c>'s inspection and handover columns.</summary>
    private readonly Dictionary<string, StoredVehicle> vehicles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Standing in for <c>dbo.ConvoyVehicle</c>.</summary>
    private readonly List<TruckListEntry> truckList = [];

    private sealed record CrewSeat(int ConvoyId, string Vin, Guid PersonId, JourneyLeg Leg, CrewRole Role);

    /// <summary>
    /// Standing in for <c>dbo.ConvoyVehicleCrew</c>, keyed as the table is: one seat per person per
    /// leg (<c>UQ_ConvoyVehicleCrew_Convoy_Person_Leg</c>).
    /// </summary>
    private readonly List<CrewSeat> crew = [];

    /// <summary>Standing in for <c>dbo.ConvoyVehicleInsurance</c>, keyed (ConvoyId, Vin).</summary>
    private readonly Dictionary<(int ConvoyId, string Vin), VehicleInsuranceReadModel> insurance = [];

    /// <summary>Names for the crew list, standing in for the join to <c>dbo.PersonDetail</c>.</summary>
    private readonly Dictionary<Guid, (string FirstName, string LastName)> persons = [];

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
        vehicles[vin] = new StoredVehicle(inspection);

        if (onConvoy is { } convoyId)
        {
            truckList.Add(new TruckListEntry(convoyId, vin, DateTime.UtcNow));
        }

        return this;
    }

    public InMemoryConvoyRepository WithPerson(Guid personId, string firstName, string lastName)
    {
        persons[personId] = (firstName, lastName);
        return this;
    }

    public InMemoryConvoyRepository WithCrew(
        string vin, Guid personId, JourneyLeg leg = JourneyLeg.Uk, CrewRole role = CrewRole.Driver)
    {
        crew.Add(new CrewSeat(
            ConvoyOf(vin) ?? throw new InvalidOperationException($"{vin} is on no convoy."), vin, personId, leg, role));
        return this;
    }

    public InMemoryConvoyRepository WithManifest(string vin, ManifestStatus status)
    {
        manifestStatus[vin] = status;
        return this;
    }

    public InMemoryConvoyRepository WithHandedOverVehicle(string vin)
    {
        vehicles[vin] = new StoredVehicle(InspectionStatus.Passed, HandedOver: true);
        return this;
    }

    public InMemoryConvoyRepository WithWithdrawnVehicle(string vin, int convoyId, string? reason = "Gearbox failure")
    {
        vehicles[vin] = new StoredVehicle(InspectionStatus.Passed);
        truckList.Add(new TruckListEntry(convoyId, vin, DateTime.UtcNow, DateTime.UtcNow, reason));
        return this;
    }

    public bool IsHandedOver(string vin) => vehicles.GetValueOrDefault(vin)?.HandedOver ?? false;

    public bool IsWithdrawn(int convoyId, string vin) => EntryFor(convoyId, vin)?.WithdrawnAt is not null;

    public InMemoryConvoyRepository WithInsurance(VehicleInsuranceReadModel policy)
    {
        insurance[(policy.ConvoyId, policy.Vin.ToUpperInvariant())] = policy;
        return this;
    }

    public VehicleInsuranceReadModel? InsuranceOf(int convoyId, string vin) =>
        insurance.GetValueOrDefault((convoyId, vin.ToUpperInvariant()));

    public int Count => convoys.Count;

    /// <summary>The convoy this vehicle is currently travelling with, as the derived SQL column reports it.</summary>
    /// <remarks>
    /// A truck-list entry whose convoy is not seeded counts as <em>not arrived</em>, not as absent.
    /// The SQL joins <c>dbo.Convoy</c> through a foreign key, so an entry without its convoy is a
    /// state the database cannot be in; treating it as free here would make the fake kinder than
    /// production and let "already on another convoy" quietly pass.
    /// </remarks>
    public int? ConvoyOf(string vin) =>
        truckList
            .Where(entry => Same(entry.Vin, vin)
                && entry.WithdrawnAt is null
                && convoys.GetValueOrDefault(entry.ConvoyId) is not { Arrived: true })
            .Select(entry => (int?)entry.ConvoyId)
            .FirstOrDefault();

    public IReadOnlyList<Guid> CrewIdsOf(int convoyId, string vin) =>
        crew.Where(seat => seat.ConvoyId == convoyId && Same(seat.Vin, vin)).Select(seat => seat.PersonId).ToList();

    public IReadOnlyList<RouteStopReadModel> RouteOf(int convoyId) => routes.GetValueOrDefault(convoyId, []);

    public bool IsOnTruckList(int convoyId, string vin) => EntryFor(convoyId, vin) is not null;

    // ---- IConvoyRepository: the journey -------------------------------------------------------

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

        // Mirrors the SQL, which touches neither stamp on an ordinary update.
        convoys[convoy.Id] = convoy with
        {
            TruckListPublishedAt = existing.TruckListPublishedAt,
            ArrivedAt = existing.ArrivedAt,
        };
        return Task.FromResult(true);
    }

    public Task<DeleteResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        if (!convoys.ContainsKey(id))
        {
            return Task.FromResult(DeleteResult.NotFound);
        }

        // FK_Manifest_ConvoyVehicle is NO ACTION: a convoy its manifests still name cannot go.
        if (VinsOn(id).Any(manifestStatus.ContainsKey))
        {
            return Task.FromResult(DeleteResult.StillReferenced);
        }

        routes.Remove(id);
        crew.RemoveAll(seat => seat.ConvoyId == id);
        foreach (var key in insurance.Keys.Where(key => key.ConvoyId == id).ToList())
        {
            insurance.Remove(key);
        }

        truckList.RemoveAll(entry => entry.ConvoyId == id);
        convoys.Remove(id);
        return Task.FromResult(DeleteResult.Deleted);
    }

    public Task<IReadOnlyList<RouteStopReadModel>> GetRouteAsync(int convoyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RouteStopReadModel>>(routes.GetValueOrDefault(convoyId, []));

    public Task ReplaceRouteAsync(
        int convoyId, IReadOnlyList<RouteStopReadModel> stops, CancellationToken cancellationToken)
    {
        routes[convoyId] = [.. stops];
        return Task.CompletedTask;
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

    public Task<ArriveResult> ArriveAsync(int convoyId, DateTime arrivedAt, CancellationToken cancellationToken)
    {
        if (!convoys.TryGetValue(convoyId, out var convoy) || convoy.Arrived || !convoy.TruckListPublished)
        {
            return Task.FromResult(ArriveResult.AlreadyArrived);
        }

        if (StillTravelling(convoyId).Count > 0)
        {
            return Task.FromResult(ArriveResult.VehiclesStillTravelling);
        }

        convoys[convoyId] = convoy with { ArrivedAt = arrivedAt };

        // Delivered and Lost vehicles stay in Ukraine. Nothing is released: a vehicle that was not
        // handed over is free for the next convoy because this one has arrived, which is what
        // ConvoyOf and AddAsync ask. Withdrawn vehicles are skipped — whatever became of them did
        // not become of them here.
        foreach (var vin in TravellingOn(convoyId))
        {
            if (manifestStatus.GetValueOrDefault(vin) is ManifestStatus.Delivered or ManifestStatus.Lost)
            {
                vehicles[vin] = vehicles[vin] with { HandedOver = true };
            }
        }

        return Task.FromResult(ArriveResult.Arrived);
    }

    public Task<IReadOnlyList<string>> ListVehiclesStillTravellingAsync(int convoyId, CancellationToken cancellationToken) =>
        Task.FromResult(StillTravelling(convoyId));

    private IReadOnlyList<string> StillTravelling(int convoyId) =>
        TravellingOn(convoyId)
            .Where(vin => manifestStatus.GetValueOrDefault(vin)
                is not (ManifestStatus.Delivered or ManifestStatus.Lost or ManifestStatus.Returned))
            .Order(StringComparer.Ordinal)
            .ToList();

    // ---- IConvoyVehicleRepository: the truck list, its crew and its insurance -----------------

    public Task<IReadOnlyList<ConvoyVehicleReadModel>> ListAsync(int convoyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConvoyVehicleReadModel>>(
            truckList
                .Where(entry => entry.ConvoyId == convoyId)
                .OrderBy(entry => entry.Vin, StringComparer.Ordinal)
                .Select(Project)
                .ToList());

    public Task<ConvoyVehicleReadModel?> GetAsync(int convoyId, string vin, CancellationToken cancellationToken) =>
        Task.FromResult(EntryFor(convoyId, vin) is { } entry ? Project(entry) : null);

    private ConvoyVehicleReadModel Project(TruckListEntry entry) => new(
        entry.Vin,
        "AB12CDE",
        1_400,
        CountOf(entry, JourneyLeg.Uk, CrewRole.Driver),
        CountOf(entry, JourneyLeg.Uk, CrewRole.Passenger),
        CountOf(entry, JourneyLeg.Border, CrewRole.Driver),
        CountOf(entry, JourneyLeg.Border, CrewRole.Passenger),
        entry.WithdrawnAt,
        entry.WithdrawnReason);

    public Task<AddToTruckListResult> AddAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        if (!vehicles.TryGetValue(vin, out var vehicle))
        {
            return Task.FromResult(AddToTruckListResult.VehicleNotFound);
        }

        if (EntryFor(convoyId, vin) is not null)
        {
            return Task.FromResult(AddToTruckListResult.AlreadyOnThisConvoy);
        }

        if (vehicle.HandedOver)
        {
            return Task.FromResult(AddToTruckListResult.HandedOver);
        }

        if (vehicle.Inspection != InspectionStatus.Passed)
        {
            return Task.FromResult(AddToTruckListResult.NotPassedInspection);
        }

        // "On another convoy" means travelling with one that has not arrived. A withdrawn vehicle,
        // or one whose convoy has arrived without handing it over, is free.
        if (ConvoyOf(vin) is { } current && current != convoyId)
        {
            return Task.FromResult(AddToTruckListResult.OnAnotherConvoy);
        }

        truckList.Add(new TruckListEntry(convoyId, vin, DateTime.UtcNow));
        return Task.FromResult(AddToTruckListResult.Added);
    }

    public Task<bool> RemoveAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        if (EntryFor(convoyId, vin) is null)
        {
            return Task.FromResult(false);
        }

        // The crew and the insurance cascade from the truck-list row.
        crew.RemoveAll(seat => seat.ConvoyId == convoyId && Same(seat.Vin, vin));
        insurance.Remove((convoyId, vin.ToUpperInvariant()));
        truckList.RemoveAll(entry => entry.ConvoyId == convoyId && Same(entry.Vin, vin));

        return Task.FromResult(true);
    }

    public Task<bool> WithdrawAsync(
        int convoyId, string vin, string? reason, DateTime withdrawnAt, CancellationToken cancellationToken)
    {
        var index = truckList.FindIndex(entry =>
            entry.ConvoyId == convoyId && Same(entry.Vin, vin) && entry.WithdrawnAt is null);

        if (index < 0)
        {
            return Task.FromResult(false);
        }

        // Everything else stays: the crew that set off, the policy it was insured under, and the
        // manifest describing what it was carrying.
        truckList[index] = truckList[index] with { WithdrawnAt = withdrawnAt, WithdrawnReason = reason };
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<VehicleCrewReadModel>?> ListCrewAsync(
        int convoyId, string vin, JourneyLeg? leg, CancellationToken cancellationToken)
    {
        if (EntryFor(convoyId, vin) is null)
        {
            return Task.FromResult<IReadOnlyList<VehicleCrewReadModel>?>(null);
        }

        var members = crew
            .Where(seat => seat.ConvoyId == convoyId && Same(seat.Vin, vin) && (leg is null || seat.Leg == leg))
            .Select(seat =>
            {
                // LEFT JOIN: an erased volunteer keeps their seat in the history, but not their name.
                var (firstName, lastName) = persons.TryGetValue(seat.PersonId, out var found)
                    ? found
                    : ("Former", "volunteer");

                return new VehicleCrewReadModel(seat.PersonId, firstName, lastName, seat.Leg, seat.Role);
            })
            .OrderBy(member => member.Leg)
            .ThenBy(member => member.Role)
            .ThenBy(member => member.LastName)
            .ThenBy(member => member.FirstName)
            .ToList();

        return Task.FromResult<IReadOnlyList<VehicleCrewReadModel>?>(members);
    }

    public Task<AssignCrewResult> AssignCrewAsync(
        int convoyId, string vin, Guid personId, JourneyLeg leg, CrewRole role, CancellationToken cancellationToken)
    {
        var seat = crew.Find(existing =>
            existing.ConvoyId == convoyId && existing.PersonId == personId && existing.Leg == leg);

        if (seat is not null)
        {
            return Task.FromResult(Same(seat.Vin, vin)
                ? AssignCrewResult.AlreadyAssigned
                : AssignCrewResult.OnAnotherVehicle);
        }

        if (EntryFor(convoyId, vin) is not { WithdrawnAt: null })
        {
            return Task.FromResult(AssignCrewResult.VehicleNotOnConvoy);
        }

        crew.Add(new CrewSeat(convoyId, vin, personId, leg, role));
        VoidInsurance(convoyId, vin);
        return Task.FromResult(AssignCrewResult.Assigned);
    }

    public Task<bool> UnassignCrewAsync(
        int convoyId, string vin, Guid personId, JourneyLeg leg, CancellationToken cancellationToken)
    {
        var removed = crew.RemoveAll(seat =>
            seat.ConvoyId == convoyId && Same(seat.Vin, vin) && seat.PersonId == personId && seat.Leg == leg) > 0;

        if (removed)
        {
            VoidInsurance(convoyId, vin);
        }

        return Task.FromResult(removed);
    }

    public Task<VehicleInsuranceReadModel?> GetInsuranceAsync(int convoyId, string vin, CancellationToken cancellationToken) =>
        Task.FromResult(InsuranceOf(convoyId, vin));

    public Task<bool> RecordInsuranceAsync(VehicleInsuranceRecord policy, CancellationToken cancellationToken)
    {
        // Mirrors the MERGE's source clause: the vehicle has to be travelling with the convoy.
        if (EntryFor(policy.ConvoyId, policy.Vin) is not { WithdrawnAt: null })
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

    // ---- helpers ------------------------------------------------------------------------------

    private void VoidInsurance(int convoyId, string vin)
    {
        if (InsuranceOf(convoyId, vin) is { VoidedAt: null } policy)
        {
            insurance[(convoyId, vin.ToUpperInvariant())] = policy with { VoidedAt = DateTime.UtcNow };
        }
    }

    private TruckListEntry? EntryFor(int convoyId, string vin) =>
        truckList.Find(entry => entry.ConvoyId == convoyId && Same(entry.Vin, vin));

    private List<string> VinsOn(int convoyId) =>
        truckList.Where(entry => entry.ConvoyId == convoyId).Select(entry => entry.Vin).ToList();

    private List<string> TravellingOn(int convoyId) =>
        truckList
            .Where(entry => entry.ConvoyId == convoyId && entry.WithdrawnAt is null)
            .Select(entry => entry.Vin)
            .ToList();

    private int CountOf(TruckListEntry entry, JourneyLeg leg, CrewRole role) =>
        crew.Count(seat =>
            seat.ConvoyId == entry.ConvoyId && Same(seat.Vin, entry.Vin) && seat.Leg == leg && seat.Role == role);

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
