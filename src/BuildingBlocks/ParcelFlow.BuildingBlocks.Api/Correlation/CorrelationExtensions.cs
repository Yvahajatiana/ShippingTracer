using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ParcelFlow.BuildingBlocks.Application.Correlation;

namespace ParcelFlow.BuildingBlocks.Api.Correlation;

public static class CorrelationExtensions
{
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>
    /// Resolves the correlation of each request from a valid <c>X-Correlation-Id</c> GUID header, or generates one,
    /// makes it the ambient <see cref="ICorrelationContext"/> of the request scope, adds it to the logging scope
    /// and echoes it in the response. Place it first so error responses carry it too.
    /// </summary>
    public static IApplicationBuilder UseParcelFlowCorrelation(this IApplicationBuilder app)
    {
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("ParcelFlow.Correlation");
        return app.Use(async (context, next) =>
        {
            var correlationId = Resolve(context.Request.Headers[HeaderName]);
            context.RequestServices.GetService<CorrelationContext>()?.Set(correlationId);
            // OnStarting survives the header reset performed before an exception is handled.
            context.Response.OnStarting(static state =>
            {
                var (response, value) = ((HttpResponse, string))state;
                response.Headers[HeaderName] = value;
                return Task.CompletedTask;
            }, (context.Response, correlationId.ToString("D")));
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
                await next(context);
        });
    }

    /// <summary>Accepts a single non-empty GUID; anything else is ignored and replaced by a new identifier.</summary>
    private static Guid Resolve(Microsoft.Extensions.Primitives.StringValues values) =>
        values.Count == 1 && Guid.TryParse(values[0], out var incoming) && incoming != Guid.Empty ? incoming : Guid.NewGuid();
}
