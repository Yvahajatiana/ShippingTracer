using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ParcelFlow.BuildingBlocks.Api.Errors;

public static class ProblemDetailsExtensions
{
    public static IServiceCollection AddParcelFlowProblemDetails(this IServiceCollection services)
    {
        services.Insert(0, ServiceDescriptor.Singleton<IProblemDetailsWriter, ParcelFlowProblemDetailsWriter>());
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions["traceId"] =
                Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        });
        services.AddExceptionHandler<ApiExceptionHandler>();
        return services;
    }

    public static IApplicationBuilder UseParcelFlowProblemDetails(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }
}
