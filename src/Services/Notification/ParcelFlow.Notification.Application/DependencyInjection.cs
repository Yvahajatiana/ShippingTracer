using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParcelFlow.BuildingBlocks.Application.Correlation;
using ParcelFlow.BuildingBlocks.Application.Validation;

namespace ParcelFlow.Notification.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationApplication(this IServiceCollection services)
    {
        services.AddParcelFlowCorrelation();
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IValidator<RecordNotification>, RecordNotificationValidator>();
        services.AddScoped<IValidator<GetNotification>, GetNotificationValidator>();
        services.AddScoped<IValidator<ListNotifications>, ListNotificationsValidator>();
        return services;
    }
}
