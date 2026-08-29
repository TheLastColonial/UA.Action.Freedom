using Microsoft.Extensions.DependencyInjection;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Application.Vehicles;

namespace UA.Action.Freedom.Application;

/// <summary>
/// Registers the use-case handlers. Each is wired explicitly rather than by assembly
/// scanning, matching the rest of the solution's <c>AddFreedom*</c> composition.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddFreedomApplication(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<CreateVehicleCommand, CreateVehicleOutcome>, CreateVehicleHandler>();
        services.AddScoped<ICommandHandler<UpdateVehicleCommand, UpdateVehicleOutcome>, UpdateVehicleHandler>();
        services.AddScoped<ICommandHandler<DeleteVehicleCommand, DeleteVehicleOutcome>, DeleteVehicleHandler>();
        services.AddScoped<IQueryHandler<GetVehicleByVinQuery, VehicleReadModel?>, GetVehicleByVinHandler>();
        services.AddScoped<IQueryHandler<ListVehiclesQuery, IReadOnlyList<VehicleReadModel>>, ListVehiclesHandler>();

        services.AddScoped<ICommandHandler<CreatePersonCommand, Guid>, CreatePersonHandler>();
        services.AddScoped<ICommandHandler<UpdatePersonCommand, UpdatePersonOutcome>, UpdatePersonHandler>();
        services.AddScoped<ICommandHandler<DeletePersonCommand, DeletePersonOutcome>, DeletePersonHandler>();
        services.AddScoped<IQueryHandler<GetPersonByIdQuery, PersonReadModel?>, GetPersonByIdHandler>();
        services.AddScoped<IQueryHandler<ListPeopleQuery, IReadOnlyList<PersonReadModel>>, ListPeopleHandler>();

        return services;
    }
}
