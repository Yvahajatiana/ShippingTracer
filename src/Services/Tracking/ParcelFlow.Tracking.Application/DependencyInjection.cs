using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.BuildingBlocks.Application.Validation;

namespace ParcelFlow.Tracking.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddTrackingApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddScoped<IValidator<RecordTrackingEvent>, RecordTrackingEventValidator>();
        services.AddScoped<IValidator<GetPublicTracking>, GetPublicTrackingValidator>();
        services.AddScoped<IValidator<GetShipmentTracking>, GetShipmentTrackingValidator>();
        return services;
    }
}
