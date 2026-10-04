using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Application.People;
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
/// vehicles; a person holds one seat per convoy (<c>UQ_ConvoyVehicleCrew_Convoy_Person</c>); crew
/// and insurance are cleared when a vehicle is removed before publication and kept when it is
/// withdrawn after; removing a driver removes them from the policy and keeps it in cover, adding one leaves them uncovered until it is recorded again; arrival ignores withdrawn vehicles and
/// releases nothing. A fake that is kinder than the SQL lets a test pass that production fails —
/// this one returned <c>[]</c> where the SQL returned <c>null</c> once already.
/// </para>
/// </remarks>
internal sealed class InMemoryConvoyRepository : IConvoyRepository, IConvoyVehicleRepository, IConvoyLeaderRepository, IRoutePointReferences, IRecordsWhoChanged
{
    private readonly ChangeLedger<int> changes = new();

    private readonly ChangeLedger<int> leaderChanges = new();

    private readonly ChangeLedger<string> ferryChanges = new(StringComparer.OrdinalIgnoreCase);

    public void Attach(IChangeAttribution attribution, IPersonRepository people)
    {
        changes.Attach(attribution, people);
        ferryChanges.Attach(attribution, people);
        leaderChanges.Attach(attribution, people);
    }

    /// <summary>Standing in for <c>dbo.ConvoyVehicleFerryBooking</c>, keyed (ConvoyId, Vin).</summary>
    private readonly Dictionary<(int ConvoyId, string Vin), FerryBookingReadModel> ferryBookings = [];

    private static string FerryKey(int convoyId, string vin) => $"{convoyId}/{vin}";

    private ConvoyReadModel Read(ConvoyReadModel convoy)
    {
        var (name, at) = changes.Of(convoy.Id);
        return convoy with { LastChangedByName = name, LastChangedAt = at };
    }

    private sealed record StoredVehicle(InspectionStatus Inspection, bool HandedOver = false);

    /// <summary>
    /// A truck-list entry, standing in for <c>dbo.ConvoyVehicle</c>. Withdrawal is a stamp, so the
    /// row outlives the vehicle leaving the convoy.
    /// </summary>
    private sealed record TruckListEntry(
        int ConvoyId, string Vin, DateTime AddedAt, DateTime? WithdrawnAt = null, string? WithdrawnReason = null,
        Guid? HandoverReceiverRef = null);

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

    private sealed record CrewSeat(int ConvoyId, string Vin, Guid PersonId, CrewRole Role);

    /// <summary>
    /// Standing in for <c>dbo.ConvoyVehicleCrew</c>, keyed as the table is: one seat per person per
    /// convoy (<c>UQ_ConvoyVehicleCrew_Convoy_Person</c>).
    /// </summary>
    private readonly List<CrewSeat> crew = [];

    /// <summary>Standing in for <c>dbo.ConvoyVehicleInsurance</c>, keyed (ConvoyId, Vin).</summary>
    private readonly Dictionary<(int ConvoyId, string Vin), VehicleInsuranceReadModel> insurance = [];

    /// <summary>Standing in for <c>dbo.ConvoyVehicleInsuranceDriver</c>: the drivers each policy names.</summary>
    private readonly Dictionary<(int ConvoyId, string Vin), HashSet<Guid>> coveredDrivers = [];

    /// <summary>Names for the crew list, standing in for the join to <c>dbo.PersonDetail</c>.</summary>
    private readonly Dictionary<Guid, (string FirstName, string LastName)> persons = [];

    private int nextId = 1;

    /// <summary>Standing in for <c>dbo.ConvoyVehicleBoxAllocation</c>; shared with the manifest fake.</summary>
    internal BoxAllocationLedger Ledger { get; set; } = new();

    public InMemoryConvoyRepository WithKnownBox(ManifestBoxReadModel box)
    {
        Ledger.KnowBox(box);
        return this;
    }

    public IReadOnlyList<BoxAllocation> Allocations => Ledger.Allocations;

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

    /// <summary>
    /// Seeds a seat as it stood when any policy was recorded: a driver seeded onto an insured vehicle
    /// is covered. A test about an uncovered driver adds them through the API instead.
    /// </summary>
    public InMemoryConvoyRepository WithCrew(string vin, Guid personId, CrewRole role = CrewRole.Driver)
    {
        var convoyId = ConvoyOf(vin) ?? throw new InvalidOperationException($"{vin} is on no convoy.");
        crew.Add(new CrewSeat(convoyId, vin, personId, role));

        if (role == CrewRole.Driver && coveredDrivers.TryGetValue((convoyId, vin.ToUpperInvariant()), out var covered))
        {
            covered.Add(personId);
        }

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

    public Guid? HandoverReceiverOf(int convoyId, string vin) => EntryFor(convoyId, vin)?.HandoverReceiverRef;

    public bool IsHandedOver(string vin) => vehicles.GetValueOrDefault(vin)?.HandedOver ?? false;

    public bool IsWithdrawn(int convoyId, string vin) => EntryFor(convoyId, vin)?.WithdrawnAt is not null;

    public InMemoryConvoyRepository WithInsurance(VehicleInsuranceReadModel policy)
    {
        insurance[(policy.ConvoyId, policy.Vin.ToUpperInvariant())] = policy;
        coveredDrivers[(policy.ConvoyId, policy.Vin.ToUpperInvariant())] = DriversOf(policy.ConvoyId, policy.Vin);
        return this;
    }

    public VehicleInsuranceReadModel? InsuranceOf(int convoyId, string vin)
    {
        var key = (convoyId, vin.ToUpperInvariant());
        if (!insurance.TryGetValue(key, out var policy))
        {
            return null;
        }

        var covered = coveredDrivers.GetValueOrDefault(key) ?? [];
        return policy with { UncoveredDrivers = DriversOf(convoyId, vin).Where(id => !covered.Contains(id)).ToList() };
    }

    private HashSet<Guid> DriversOf(int convoyId, string vin) =>
        crew.Where(seat => seat.ConvoyId == convoyId && Same(seat.Vin, vin) && seat.Role == CrewRole.Driver)
            .Select(seat => seat.PersonId)
            .ToHashSet();

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
        Task.FromResult(convoys.TryGetValue(id, out var convoy) ? Read(convoy) : null);

    public Task<IReadOnlyList<ConvoyReadModel>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConvoyReadModel>>(
            convoys.Values
                .OrderByDescending(convoy => convoy.Start)
                .ThenByDescending(convoy => convoy.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(Read)
                .ToList());

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(convoys.ContainsKey(id));

    public Task<int> AddAsync(
        DateTime start,
        DateTime expectedEnd,
        CancellationToken cancellationToken,
        ChannelCrossing crossingMode = ChannelCrossing.Ferry,
        string? vesselImo = null)
    {
        var id = nextId++;
        convoys[id] = new ConvoyReadModel(
            id, start, expectedEnd, TruckListPublishedAt: null, ArrivedAt: null,
            CrossingMode: crossingMode, VesselImo: vesselImo);
        changes.Stamp(id);
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
        changes.Stamp(convoy.Id);
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
            coveredDrivers.Remove(key);
        }

        truckList.RemoveAll(entry => entry.ConvoyId == id);
        leaderAssignments.RemoveAll(assignment => assignment.ConvoyId == id);
        changes.Forget(id);
        convoys.Remove(id);
        return Task.FromResult(DeleteResult.Deleted);
    }

    public Task<IReadOnlyList<RouteStopReadModel>> GetRouteAsync(int convoyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RouteStopReadModel>>(routes.GetValueOrDefault(convoyId, []));

    /// <summary>Standing in for <c>dbo.ConvoyLeaderAssignment</c>, newest last.</summary>
    private readonly List<ConvoyLeaderAssignmentReadModel> leaderAssignments = [];

    public Task<IReadOnlyList<ConvoyLeaderAssignmentReadModel>> HistoryAsync(int convoyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConvoyLeaderAssignmentReadModel>>(
            [.. leaderAssignments.Where(a => a.ConvoyId == convoyId).OrderByDescending(a => a.Id)]);

    public Task<bool> IsCurrentLeaderAsync(int convoyId, Guid personId, CancellationToken cancellationToken) =>
        Task.FromResult(leaderAssignments.Exists(a => a.ConvoyId == convoyId && a.PersonId == personId && a.Until is null));

    public Task<NominateLeaderResult> NominateAsync(
        int convoyId, Guid personId, DateTime at, CancellationToken cancellationToken)
    {
        if (leaderAssignments.Exists(a => a.ConvoyId == convoyId && a.PersonId == personId && a.Until is null))
        {
            return Task.FromResult(NominateLeaderResult.AlreadyLeader);
        }

        if (!crew.Exists(seat => seat.ConvoyId == convoyId && seat.PersonId == personId && seat.Role == CrewRole.Driver))
        {
            return Task.FromResult(NominateLeaderResult.NotADriverOnConvoy);
        }

        var open = leaderAssignments.FindIndex(a => a.ConvoyId == convoyId && a.Until is null);
        if (open >= 0)
        {
            leaderAssignments[open] = leaderAssignments[open] with { Until = at };
        }

        var (first, last) = persons.TryGetValue(personId, out var found) ? found : PersonDisplay.Erased;
        var id = leaderAssignments.Count + 1;
        leaderChanges.Stamp(id);
        var (name, stampedAt) = leaderChanges.Of(id);
        leaderAssignments.Add(new ConvoyLeaderAssignmentReadModel(id, convoyId, personId, $"{first} {last}", at, null, name, stampedAt));
        return Task.FromResult(NominateLeaderResult.Nominated);
    }

    /// <summary>Standing in for <c>IDENTITY</c> on <c>dbo.ConvoyRouteStop.RoutePointId</c>.</summary>
    private int lastRoutePointId;

    private readonly HashSet<int> referencedRoutePoints = [];

    /// <summary>Stands in for a later feature (accommodation, a progress mark) that refers to a route point.</summary>
    public void ReferenceRoutePoint(int routePointId) => referencedRoutePoints.Add(routePointId);

    public Task<bool> AnyAsync(int convoyId, IReadOnlyCollection<int> routePointIds, CancellationToken cancellationToken) =>
        Task.FromResult(routePointIds.Any(referencedRoutePoints.Contains));

    public Task ReplaceRouteAsync(
        int convoyId, IReadOnlyList<RouteStopReadModel> stops, CancellationToken cancellationToken)
    {
        // A merge, as the SQL is: a point named by id keeps it, a new one is given the next, the rest go.
        routes[convoyId] =
        [
            .. stops.Select(stop => stop.RoutePointId != 0 ? stop : stop with { RoutePointId = ++lastRoutePointId })
        ];
        return Task.CompletedTask;
    }

    public Task<bool> PublishTruckListAsync(int convoyId, DateTime publishedAt, CancellationToken cancellationToken)
    {
        if (!convoys.TryGetValue(convoyId, out var convoy) || convoy.TruckListPublished)
        {
            return Task.FromResult(false);
        }

        convoys[convoyId] = convoy with { TruckListPublishedAt = publishedAt };
        changes.Stamp(convoyId);
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
        changes.Stamp(convoyId);

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
        CountOf(entry, CrewRole.Driver),
        CountOf(entry, CrewRole.Passenger),
        entry.WithdrawnAt,
        entry.WithdrawnReason,
        entry.HandoverReceiverRef);

    public Task<bool> SetHandoverReceiverAsync(
        int convoyId, string vin, Guid receiverRef, CancellationToken cancellationToken)
    {
        // Mirrors the SQL: only a vehicle still travelling with the convoy can be given a handover receiver.
        var index = truckList.FindIndex(entry =>
            entry.ConvoyId == convoyId && Same(entry.Vin, vin) && entry.WithdrawnAt is null);

        if (index < 0)
        {
            return Task.FromResult(false);
        }

        truckList[index] = truckList[index] with { HandoverReceiverRef = receiverRef };
        return Task.FromResult(true);
    }

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
        coveredDrivers.Remove((convoyId, vin.ToUpperInvariant()));
        Ledger.ForgetEntry(convoyId, vin);
        ferryBookings.Remove((convoyId, vin.ToUpperInvariant()));
        ferryChanges.Forget(FerryKey(convoyId, vin));
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
        int convoyId, string vin, CancellationToken cancellationToken)
    {
        if (EntryFor(convoyId, vin) is null)
        {
            return Task.FromResult<IReadOnlyList<VehicleCrewReadModel>?>(null);
        }

        var members = crew
            .Where(seat => seat.ConvoyId == convoyId && Same(seat.Vin, vin))
            .Select(seat =>
            {
                // An erased volunteer keeps their seat in the history, but not their name.
                var (firstName, lastName) = persons.TryGetValue(seat.PersonId, out var found)
                    ? found
                    : PersonDisplay.Erased;

                return new VehicleCrewReadModel(seat.PersonId, firstName, lastName, seat.Role);
            })
            .OrderBy(member => member.Role)
            .ThenBy(member => member.LastName)
            .ThenBy(member => member.FirstName)
            .ToList();

        return Task.FromResult<IReadOnlyList<VehicleCrewReadModel>?>(members);
    }

    public Task<AssignCrewResult> AssignCrewAsync(
        int convoyId, string vin, Guid personId, CrewRole role, CancellationToken cancellationToken)
    {
        var seat = crew.Find(existing =>
            existing.ConvoyId == convoyId && existing.PersonId == personId);

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

        crew.Add(new CrewSeat(convoyId, vin, personId, role));
        return Task.FromResult(AssignCrewResult.Assigned);
    }

    public Task<bool> UnassignCrewAsync(
        int convoyId, string vin, Guid personId, CancellationToken cancellationToken)
    {
        var removed = crew.RemoveAll(seat =>
            seat.ConvoyId == convoyId && Same(seat.Vin, vin) && seat.PersonId == personId) > 0;

        if (removed && coveredDrivers.TryGetValue((convoyId, vin.ToUpperInvariant()), out var covered))
        {
            covered.Remove(personId);
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
        coveredDrivers[(policy.ConvoyId, policy.Vin.ToUpperInvariant())] = DriversOf(policy.ConvoyId, policy.Vin);
        return Task.FromResult(true);
    }

    public Task<bool> RemoveInsuranceAsync(int convoyId, string vin, CancellationToken cancellationToken) =>
        Task.FromResult(RemovePolicy(convoyId, vin));

    public Task<FerryBookingReadModel?> GetFerryBookingAsync(
        int convoyId, string vin, CancellationToken cancellationToken)
    {
        if (!ferryBookings.TryGetValue((convoyId, vin.ToUpperInvariant()), out var booking))
        {
            return Task.FromResult<FerryBookingReadModel?>(null);
        }

        var (name, at) = ferryChanges.Of(FerryKey(convoyId, vin));
        return Task.FromResult<FerryBookingReadModel?>(booking with { LastChangedByName = name, LastChangedAt = at });
    }

    public Task<bool> RecordFerryBookingAsync(FerryBookingRecord booking, CancellationToken cancellationToken)
    {
        // Mirrors the MERGE source clause: the vehicle has to be travelling with the convoy.
        if (EntryFor(booking.ConvoyId, booking.Vin) is not { WithdrawnAt: null })
        {
            return Task.FromResult(false);
        }

        ferryBookings[(booking.ConvoyId, booking.Vin.ToUpperInvariant())] = new FerryBookingReadModel(
            booking.ConvoyId, booking.Vin, booking.Operator, booking.Reference, booking.SailingAt,
            booking.TicketDetails, booking.CostGbp);
        ferryChanges.Stamp(FerryKey(booking.ConvoyId, booking.Vin));
        return Task.FromResult(true);
    }

    public Task<bool> RemoveFerryBookingAsync(int convoyId, string vin, CancellationToken cancellationToken)
    {
        ferryChanges.Forget(FerryKey(convoyId, vin));
        return Task.FromResult(ferryBookings.Remove((convoyId, vin.ToUpperInvariant())));
    }

    public Task<IReadOnlyList<ManifestBoxReadModel>?> ListBoxesAsync(
        int convoyId, string vin, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ManifestBoxReadModel>?>(
            EntryFor(convoyId, vin) is null ? null : Ledger.BoxesOn(convoyId, vin));

    public Task<BoxAllocation?> GetBoxAllocationAsync(int boxId, CancellationToken cancellationToken) =>
        Task.FromResult(Ledger.AllocationOf(boxId));

    public Task<AllocateBoxResult> AllocateBoxAsync(
        int convoyId, string vin, int boxId, CancellationToken cancellationToken)
    {
        if (EntryFor(convoyId, vin) is not { } entry)
        {
            return Task.FromResult(AllocateBoxResult.VehicleNotOnConvoy);
        }

        if (entry.WithdrawnAt is not null)
        {
            return Task.FromResult(AllocateBoxResult.VehicleWithdrawn);
        }

        if (Ledger.IsVoided(boxId))
        {
            return Task.FromResult(AllocateBoxResult.BoxVoided);
        }

        if (!Ledger.Knows(boxId))
        {
            return Task.FromResult(AllocateBoxResult.BoxNotFound);
        }

        var outcome = Ledger.Allocate(AsDomain(entry), boxId);

        return Task.FromResult(outcome switch
        {
            AllocateOutcome.Allocated => AllocateBoxResult.Allocated,
            AllocateOutcome.Moved => AllocateBoxResult.Moved,
            _ => AllocateBoxResult.AlreadyAllocated,
        });
    }

    public Task<bool> RemoveBoxAsync(int convoyId, string vin, int boxId, CancellationToken cancellationToken) =>
        Task.FromResult(EntryFor(convoyId, vin) is { } entry && Ledger.Remove(AsDomain(entry), boxId));

    private static ConvoyVehicle AsDomain(TruckListEntry entry) =>
        new() { ConvoyId = new ConvoyId(entry.ConvoyId), Vin = entry.Vin, WithdrawnAt = entry.WithdrawnAt };

    // ---- helpers ------------------------------------------------------------------------------

    private bool RemovePolicy(int convoyId, string vin)
    {
        coveredDrivers.Remove((convoyId, vin.ToUpperInvariant()));
        return insurance.Remove((convoyId, vin.ToUpperInvariant()));
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

    private int CountOf(TruckListEntry entry, CrewRole role) =>
        crew.Count(seat => seat.ConvoyId == entry.ConvoyId && Same(seat.Vin, entry.Vin) && seat.Role == role);

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
