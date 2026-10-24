using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParcelFlow.BuildingBlocks.Application.Validation;

namespace ParcelFlow.Warehouse.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddWarehouseApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IValidator<CreateWarehouse>, CreateWarehouseValidator>();
        services.AddScoped<IValidator<UpdateWarehouse>, UpdateWarehouseValidator>();
        services.AddScoped<IValidator<ScanParcel>, ScanParcelValidator>();
        services.AddScoped<IValidator<RegisterReadyShipment>, RegisterReadyShipmentValidator>();
        services.AddScoped<IValidator<RecordDeliveryExit>, RecordDeliveryExitValidator>();
        services.AddScoped<IValidator<GetWarehouse>,GetWarehouseValidator>();
        services.AddScoped<IValidator<ListWarehouses>, ListWarehousesValidator>();
        services.AddScoped<IValidator<GetWarehouseStock>, GetWarehouseStockValidator>();
        return services;
    }
}
