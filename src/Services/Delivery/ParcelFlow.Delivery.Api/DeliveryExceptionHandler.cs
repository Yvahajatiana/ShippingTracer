using Microsoft.AspNetCore.Diagnostics;
using ParcelFlow.BuildingBlocks.Api.Errors;
using ParcelFlow.Delivery.Application;

namespace ParcelFlow.Delivery.Api;

// Maps Delivery business exceptions only; malformed requests, domain guards and open circuits
// fall through to the shared ApiExceptionHandler.
public sealed class DeliveryExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            DeliveryNotFoundException => (404, "Resource not found"),
            DeliveryConflictException => (409, "Resource conflict"),
            DeliveryForbiddenException => (403, "Access denied"),
            _ => (0, string.Empty)
        };
        if (status == 0) return false;
        await problemDetailsService.WriteProblemAsync(context, status, title);
        return true;
    }
}
