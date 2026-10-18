using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParcelFlow.BuildingBlocks.Application.Validation;

namespace ParcelFlow.Delivery.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddDeliveryApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IValidator<CreateDriver>, CreateDriverValidator>();
        services.AddScoped<IValidator<UpdateDriver>, UpdateDriverValidator>();
        services.AddScoped<IValidator<CreateRoute>, CreateRouteValidator>();
        services.AddScoped<IValidator<AssignRouteDriver>, AssignRouteDriverValidator>();
        services.AddScoped<IValidator<AddRouteParcel>, AddRouteParcelValidator>();
        services.AddScoped<IValidator<RemoveRouteParcel>, RemoveRouteParcelValidator>();
        services.AddScoped<IValidator<StartRoute>, StartRouteValidator>();
        services.AddScoped<IValidator<RegisterReadyShipment>, RegisterReadyShipmentValidator>();
        services.AddScoped<IValidator<RegisterParcelPickup>, RegisterParcelPickupValidator>();
        services.AddScoped<IValidator<ConfirmDelivery>, ConfirmDeliveryValidator>();
        services.AddScoped<IValidator<FailDelivery>, FailDeliveryValidator>();
        services.AddScoped<IValidator<GetRoute>, GetRouteValidator>();
        services.AddScoped<IValidator<GetDeliveryProof>, GetDeliveryProofValidator>();
        services.AddScoped<IValidator<GetDeliveryProofImage>, GetDeliveryProofImageValidator>();
        services.AddScoped<IValidator<ListRoutes>, ListRoutesValidator>();
        services.AddScoped<IValidator<ListDrivers>, ListDriversValidator>();
        services.AddScoped<IValidator<ListAvailableParcels>, ListAvailableParcelsValidator>();
        return services;
    }
}
