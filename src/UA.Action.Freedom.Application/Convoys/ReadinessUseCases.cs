using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>Whether one vehicle on a convoy is ready to travel, and if not, why.</summary>
public sealed record VehicleReadinessReadModel(
    string Vin,
    string Plate,
    bool Insured,
    bool Ready,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Advisories);

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
/// A vehicle is ready when it has at least <see cref="DriversRequired"/> driver — passengers do not
/// count — and its insurance is recorded, not voided, in cover on the day the convoy departs and
/// names every driver. <see cref="DriversAdvised"/> drivers are advised; fewer is an advisory, not a
/// reason. A convoy is ready when it has a route, has vehicles still travelling with it, and every
/// one of them is ready.
///
/// <para>
/// A withdrawn vehicle is skipped entirely rather than reported as unready. It broke down and left;
/// it has no crew to find and no insurance to renew, so listing it as a problem would be reporting
/// something nobody can fix and would keep the convoy permanently un-ready.
/// </para>
/// </remarks>
public static class ConvoyReadiness
{
    public const int DriversRequired = 1;

    public const int DriversAdvised = 2;

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
        var insuranceProblem = policy switch
        {
            null => "Insurance not recorded",
            { Voided: true } => "Insurance voided",
            _ when !policy.CoversOn(departs) => "Insurance does not cover the departure date",
            { CoversAllDrivers: false } => "Insurance does not cover every driver",
            _ => null,
        };

        string[] reasons =
        [
            .. vehicle.DriverCount >= DriversRequired ? [] : new[] { "No driver assigned" },
            .. insuranceProblem is null ? [] : new[] { insuranceProblem },
        ];

        string[] advisories = vehicle.DriverCount is >= DriversRequired and < DriversAdvised
            ? ["Only one driver; two are advised"]
            : [];

        return new VehicleReadinessReadModel(
            vehicle.Vin, vehicle.Plate, insuranceProblem is null, reasons.Length == 0, reasons, advisories);
    }
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
