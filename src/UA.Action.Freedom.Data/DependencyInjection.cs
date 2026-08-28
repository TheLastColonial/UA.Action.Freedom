using Microsoft.Extensions.DependencyInjection;
using UA.Action.Freedom.Application.Vehicles;
using UA.Action.Freedom.Data.Vehicles;

namespace UA.Action.Freedom.Data;

/// <summary>
/// Wires the persistence adapters. The connection factory is stateless (it only reads
/// configuration) so it is a singleton; repositories are scoped to a request.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddFreedomData(this IServiceCollection services)
    {
        services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();

        return services;
    }
}
