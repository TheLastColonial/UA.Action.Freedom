using Microsoft.Extensions.DependencyInjection;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Application.Receivers;
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

        services.AddScoped<ICommandHandler<CreateConvoyCommand, int>, CreateConvoyHandler>();
        services.AddScoped<ICommandHandler<UpdateConvoyCommand, UpdateConvoyOutcome>, UpdateConvoyHandler>();
        services.AddScoped<ICommandHandler<DeleteConvoyCommand, DeleteConvoyOutcome>, DeleteConvoyHandler>();
        services.AddScoped<IQueryHandler<GetConvoyByIdQuery, ConvoyReadModel?>, GetConvoyByIdHandler>();
        services.AddScoped<IQueryHandler<ListConvoysQuery, IReadOnlyList<ConvoyReadModel>>, ListConvoysHandler>();
        services.AddScoped<IQueryHandler<GetConvoyRouteQuery, IReadOnlyList<RouteStopReadModel>?>, GetConvoyRouteHandler>();
        services.AddScoped<ICommandHandler<ReplaceConvoyRouteCommand, ReplaceConvoyRouteOutcome>, ReplaceConvoyRouteHandler>();
        services.AddScoped<ICommandHandler<PublishTruckListCommand, PublishTruckListOutcome>, PublishTruckListHandler>();
        services.AddScoped<ICommandHandler<AssignVehicleToConvoyCommand, AssignVehicleOutcome>, AssignVehicleToConvoyHandler>();
        services.AddScoped<ICommandHandler<UnassignVehicleFromConvoyCommand, UnassignVehicleOutcome>, UnassignVehicleFromConvoyHandler>();
        services.AddScoped<IQueryHandler<ListConvoyVehiclesQuery, IReadOnlyList<ConvoyVehicleReadModel>?>, ListConvoyVehiclesHandler>();

        services.AddScoped<ICommandHandler<CreateReceiverCommand, Guid>, CreateReceiverHandler>();
        services.AddScoped<ICommandHandler<UpdateReceiverCommand, UpdateReceiverOutcome>, UpdateReceiverHandler>();
        services.AddScoped<ICommandHandler<DeleteReceiverCommand, DeleteReceiverOutcome>, DeleteReceiverHandler>();
        services.AddScoped<IQueryHandler<GetReceiverByRefQuery, ReceiverReadModel?>, GetReceiverByRefHandler>();
        services.AddScoped<IQueryHandler<ListReceiversQuery, IReadOnlyList<ReceiverReadModel>>, ListReceiversHandler>();
        services.AddScoped<IQueryHandler<GetReceiverDetailQuery, ReceiverDetailReadModel?>, GetReceiverDetailHandler>();
        services.AddScoped<ICommandHandler<SetReceiverDetailCommand, SetReceiverDetailOutcome>, SetReceiverDetailHandler>();

        services.AddScoped<ICommandHandler<CreateBoxCommand, int>, CreateBoxHandler>();
        services.AddScoped<ICommandHandler<UpdateBoxCommand, UpdateBoxOutcome>, UpdateBoxHandler>();
        services.AddScoped<ICommandHandler<DeleteBoxCommand, DeleteBoxOutcome>, DeleteBoxHandler>();
        services.AddScoped<IQueryHandler<GetBoxByIdQuery, BoxReadModel?>, GetBoxByIdHandler>();
        services.AddScoped<IQueryHandler<ListBoxesQuery, IReadOnlyList<BoxReadModel>>, ListBoxesHandler>();
        services.AddScoped<ICommandHandler<ValidateBoxCommand, ValidateBoxOutcome>, ValidateBoxHandler>();
        services.AddScoped<IQueryHandler<ListBoxItemsQuery, IReadOnlyList<BoxItemReadModel>?>, ListBoxItemsHandler>();
        services.AddScoped<ICommandHandler<AddBoxItemCommand, AddBoxItemOutcome>, AddBoxItemHandler>();
        services.AddScoped<ICommandHandler<RemoveBoxItemCommand, RemoveBoxItemOutcome>, RemoveBoxItemHandler>();

        return services;
    }
}
