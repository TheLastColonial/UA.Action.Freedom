using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Convoys;

/// <summary>Whether one vehicle on a convoy is ready to travel, and if not, why.</summary>
public sealed record VehicleReadinessReadModel(
    string Vin,
    string Plate,
    int Drivers,
    bool Insured,
    bool Ready,
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
/// A vehicle is ready with at least <see cref="DriversNeeded"/> drivers — passengers do not
/// count — and insurance that is recorded, not voided by a crew change, and in cover on the day
/// the convoy departs. A convoy is ready when it has a route, has vehicles, and every one of them
/// is ready. Cargo checks will join these later.
/// </remarks>
public static class ConvoyReadiness
{
    public const int DriversNeeded = 2;

    public static ConvoyReadinessReadModel Assess(
        DateTime departs,
        bool routePlanned,
        IReadOnlyList<(ConvoyVehicleReadModel Vehicle, VehicleInsuranceReadModel? Policy)> vehicles)
    {
        var assessed = vehicles.Select(entry => AssessVehicle(departs, entry.Vehicle, entry.Policy)).ToList();
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
            { Voided: true } => "Insurance voided by a crew change",
            _ when !policy.CoversOn(departs) => "Insurance does not cover the departure date",
            _ => null,
        };

        string[] reasons =
        [
            .. vehicle.DriverCount >= DriversNeeded ? [] : new[] { "Fewer than two drivers" },
            .. insuranceProblem is null ? [] : new[] { insuranceProblem },
        ];

        return new VehicleReadinessReadModel(
            vehicle.Vin, vehicle.Plate, vehicle.DriverCount, insuranceProblem is null, reasons.Length == 0, reasons);
    }
}

public sealed record GetConvoyReadinessQuery(int ConvoyId);

/// <summary>The convoy's readiness, or null when there is no such convoy.</summary>
public sealed class GetConvoyReadinessHandler(IConvoyRepository repository)
    : IQueryHandler<GetConvoyReadinessQuery, ConvoyReadinessReadModel?>
{
    public async Task<ConvoyReadinessReadModel?> HandleAsync(
        GetConvoyReadinessQuery query, CancellationToken cancellationToken)
    {
        var convoy = await repository.GetByIdAsync(query.ConvoyId, cancellationToken);
        if (convoy is null)
        {
            return null;
        }

        var route = await repository.GetRouteAsync(query.ConvoyId, cancellationToken);
        var vehicles = await repository.ListVehiclesAsync(query.ConvoyId, cancellationToken);

        // One read per vehicle: a convoy is a handful of vans, so this stays cheap and simple.
        var withPolicies = new List<(ConvoyVehicleReadModel, VehicleInsuranceReadModel?)>();
        foreach (var vehicle in vehicles)
        {
            withPolicies.Add((vehicle, await repository.GetInsuranceAsync(query.ConvoyId, vehicle.Vin, cancellationToken)));
        }

        return ConvoyReadiness.Assess(convoy.Start, route.Count > 0, withPolicies);
    }
}
