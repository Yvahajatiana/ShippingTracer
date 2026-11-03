using Microsoft.AspNetCore.Diagnostics;
using ParcelFlow.BuildingBlocks.Api.Errors;
using ParcelFlow.Tracking.Application;

namespace ParcelFlow.Tracking.Api;

// Maps Tracking business exceptions only; malformed requests, domain guards and open circuits
// fall through to the shared ApiExceptionHandler.
public sealed class TrackingExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            TrackingNotFoundException => (404, "Resource not found"),
            TrackingConflictException => (409, "Resource conflict"),
            _ => (0, string.Empty)
        };
        if (status == 0) return false;
        await problemDetailsService.WriteProblemAsync(context, status, title);
        return true;
    }
}
