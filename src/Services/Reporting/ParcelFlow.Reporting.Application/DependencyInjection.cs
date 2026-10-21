using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParcelFlow.BuildingBlocks.Application.Validation;

namespace ParcelFlow.Reporting.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddReportingApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IValidator<RecordStatusFact>, RecordStatusFactValidator>();
        services.AddScoped<IValidator<GetKpis>, GetKpisValidator>();
        return services;
    }
}
