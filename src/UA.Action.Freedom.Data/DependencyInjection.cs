using Microsoft.Extensions.DependencyInjection;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Application.Vehicles;
using UA.Action.Freedom.Data.Boxes;
using UA.Action.Freedom.Data.Convoys;
using UA.Action.Freedom.Data.People;
using UA.Action.Freedom.Data.Receivers;
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
        services.AddScoped<IPersonRepository, PersonRepository>();
        services.AddScoped<IConvoyRepository, ConvoyRepository>();
        services.AddScoped<IReceiverRepository, ReceiverRepository>();
        services.AddScoped<IBoxRepository, BoxRepository>();

        // The Ground Officer path to Ukrainian delivery detail. A second connection factory,
        // bound to a database identity in the ground_officer role — the application's own
        // identity is DENY'd on the sensitive schema, so this is the only way through
        // (docs/recommendations.md 4.4).
        services.AddSingleton<ISensitiveDbConnectionFactory, SensitiveSqlConnectionFactory>();
        services.AddScoped<IReceiverDetailRepository, ReceiverDetailRepository>();

        return services;
    }
}
