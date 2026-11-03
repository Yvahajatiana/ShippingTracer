using Microsoft.AspNetCore.Diagnostics;
using ParcelFlow.BuildingBlocks.Api.Errors;
using ParcelFlow.Notification.Application;

namespace ParcelFlow.Notification.Api;

// Maps Notification business exceptions only; malformed requests, domain guards and open circuits
// fall through to the shared ApiExceptionHandler.
public sealed class NotificationExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            NotificationNotFoundException => (404, "Resource not found"),
            NotificationConflictException => (409, "Resource conflict"),
            NotificationForbiddenException => (403, "Access denied"),
            _ => (0, string.Empty)
        };
        if (status == 0) return false;
        await problemDetailsService.WriteProblemAsync(context, status, title);
        return true;
    }
}
