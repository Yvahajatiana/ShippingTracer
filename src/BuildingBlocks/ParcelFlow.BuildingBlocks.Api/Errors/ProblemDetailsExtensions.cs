using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

    /// <summary>
    /// Marks every response under <paramref name="pathPrefix"/> (default <c>/api</c>) as non-cacheable,
    /// including responses rewritten by the exception handler or the status code pages.
    /// </summary>
    public static IApplicationBuilder UseParcelFlowNoStore(this IApplicationBuilder app, PathString? pathPrefix = null)
    {
        var prefix = pathPrefix ?? new PathString("/api");
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(prefix))
            {
                // OnStarting survives the header reset performed before an exception is handled.
                context.Response.OnStarting(static state =>
                {
                    ((HttpResponse)state).Headers.CacheControl = "no-store";
                    return Task.CompletedTask;
                }, context.Response);
            }
            await next(context);
        });
    }

    /// <summary>Writes a service-specific error with the shared ParcelFlow ProblemDetails shape.</summary>
    public static ValueTask WriteProblemAsync(this IProblemDetailsService problemDetailsService,
        HttpContext context, int status, string title)
    {
        context.Response.StatusCode = status;
        return problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status, Title = title, Instance = context.Request.Path,
                Type = $"https://httpstatuses.io/{status}"
            }
        });
    }
}
