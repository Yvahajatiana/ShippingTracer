using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.BuildingBlocks.Application.Validation;

namespace ParcelFlow.Shipment.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddShipmentApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddScoped<ShipmentAccess>();
        services.AddScoped<IValidator<CreateShipment>, CreateShipmentValidator>();
        services.AddScoped<IValidator<QuoteShipment>, QuoteShipmentValidator>();
        services.AddScoped<IValidator<GetShipment>, GetShipmentValidator>();
        services.AddScoped<IValidator<GetLabel>, GetLabelValidator>();
        services.AddScoped<IValidator<ListShipments>, ListShipmentsValidator>();
        services.AddScoped<IValidator<UpdateTariff>, UpdateTariffValidator>();
        return services;
    }
}
