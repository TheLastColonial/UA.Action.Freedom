using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>Whether one leg of one vehicle's journey is crewed, and if not, why.</summary>
public sealed record VehicleLegReadinessReadModel(
    JourneyLeg Leg,
    int Drivers,
    bool Ready,
    IReadOnlyList<string> Reasons);

/// <summary>Whether one vehicle on a convoy is ready to travel, and if not, why.</summary>
public sealed record VehicleReadinessReadModel(
    string Vin,
    string Plate,
    bool Insured,
    bool Ready,
    IReadOnlyList<VehicleLegReadinessReadModel> Legs,
    IReadOnlyList<string> Reasons);

/// <summary>Whether a convoy is ready to travel, and if not, why. Advisory: nothing is blocked by it.</summary>
public sealed record ConvoyReadinessReadModel(
    bool Ready,
    bool RoutePlanned,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<VehicleReadinessReadModel> Vehicles);

/// <summary>
/// The readiness rules, as one pure function so they can be read and tested in one place.
/// </summary>
/// <remarks>
/// A vehicle is ready when every leg has at least <see cref="DriversNeeded"/> drivers — passengers
/// do not count — and its insurance is recorded, not voided by a crew change, and in cover on the
/// day the convoy departs. A convoy is ready when it has a route, has vehicles still travelling
/// with it, and every one of them is ready.
///
/// <para>
/// Crew is asked for per leg because a vehicle is crewed twice, with a handover at the European
/// border in between: fully crewed out of the UK with nobody booked to take it into Ukraine is not
/// ready, and a single count could not say so.
/// </para>
///
/// <para>
/// A withdrawn vehicle is skipped entirely rather than reported as unready. It broke down and left;
/// it has no crew to find and no insurance to renew, so listing it as a problem would be reporting
/// something nobody can fix and would keep the convoy permanently un-ready.
/// </para>
/// </remarks>
public static class ConvoyReadiness
{
    public const int DriversNeeded = 2;

    /// <summary>The legs of the journey, in the order they are driven.</summary>
    private static readonly JourneyLeg[] Legs = [JourneyLeg.Uk, JourneyLeg.Border];

    public static ConvoyReadinessReadModel Assess(
        DateTime departs,
        bool routePlanned,
        IReadOnlyList<(ConvoyVehicleReadModel Vehicle, VehicleInsuranceReadModel? Policy)> vehicles)
    {
        var assessed = vehicles
            .Where(entry => entry.Vehicle.Travelling)
            .Select(entry => AssessVehicle(departs, entry.Vehicle, entry.Policy))
            .ToList();

        var notReady = assessed.Count(vehicle => !vehicle.Ready);

        string[] reasons =
        [
            .. routePlanned ? [] : new[] { "No route planned" },
            .. assessed.Count > 0 ? [] : new[] { "No vehicles on the truck list" },
            .. notReady == 0 ? [] : new[] { notReady == 1 ? "1 vehicle not ready" : $"{notReady} vehicles not ready" },
        ];

        return new ConvoyReadinessReadModel(reasons.Length == 0, routePlanned, reasons, assessed);
    }

    private static VehicleReadinessReadModel AssessVehicle(
        DateTime departs, ConvoyVehicleReadModel vehicle, VehicleInsuranceReadModel? policy)
    {
        var legs = Legs.Select(leg => AssessLeg(leg, vehicle.DriversOn(leg))).ToList();

        var insuranceProblem = policy switch
        {
            null => "Insurance not recorded",
            { Voided: true } => "Insurance voided by a crew change",
            _ when !policy.CoversOn(departs) => "Insurance does not cover the departure date",
            _ => null,
        };

        string[] reasons =
        [
            .. legs.SelectMany(leg => leg.Reasons),
            .. insuranceProblem is null ? [] : new[] { insuranceProblem },
        ];

        return new VehicleReadinessReadModel(
            vehicle.Vin, vehicle.Plate, insuranceProblem is null, reasons.Length == 0, legs, reasons);
    }

    private static VehicleLegReadinessReadModel AssessLeg(JourneyLeg leg, int drivers)
    {
        string[] reasons = drivers >= DriversNeeded
            ? []
            : [$"Fewer than two drivers on the {Describe(leg)} leg"];

        return new VehicleLegReadinessReadModel(leg, drivers, reasons.Length == 0, reasons);
    }

    /// <summary>
    /// The leg as a dispatcher would say it out loud. The enum names are for code; a reason shown
    /// on the convoy overview has to name the half of the journey somebody has to crew.
    /// </summary>
    private static string Describe(JourneyLeg leg) => leg switch
    {
        JourneyLeg.Uk => "UK to Europe",
        _ => "Europe to Ukraine",
    };
}

public sealed record GetConvoyReadinessQuery(int ConvoyId);

/// <summary>The convoy's readiness, or null when there is no such convoy.</summary>
public sealed class GetConvoyReadinessHandler(IConvoyRepository convoys, IConvoyVehicleRepository truckList)
    : IQueryHandler<GetConvoyReadinessQuery, ConvoyReadinessReadModel?>
{
    public async Task<ConvoyReadinessReadModel?> HandleAsync(
        GetConvoyReadinessQuery query, CancellationToken cancellationToken)
    {
        var convoy = await convoys.GetByIdAsync(query.ConvoyId, cancellationToken);
        if (convoy is null)
        {
            return null;
        }

        var route = await convoys.GetRouteAsync(query.ConvoyId, cancellationToken);
        var vehicles = await truckList.ListAsync(query.ConvoyId, cancellationToken);

        // One read per vehicle: a convoy is a handful of vans, so this stays cheap and simple.
        var withPolicies = new List<(ConvoyVehicleReadModel, VehicleInsuranceReadModel?)>();
        foreach (var vehicle in vehicles)
        {
            withPolicies.Add((vehicle, await truckList.GetInsuranceAsync(query.ConvoyId, vehicle.Vin, cancellationToken)));
        }

        return ConvoyReadiness.Assess(convoy.Start, route.Count > 0, withPolicies);
    }
}
