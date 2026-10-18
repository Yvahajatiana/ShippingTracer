using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ParcelFlow.Notification.Application;

namespace ParcelFlow.Notification.Api;

public sealed class NotificationExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            NotificationNotFoundException => (404, "Resource not found"),
            NotificationConflictException => (409, "Resource conflict"),
            NotificationForbiddenException => (403, "Access denied"),
            BadHttpRequestException { StatusCode: 413 } => (413, "Request too large"),
            JsonException or BadHttpRequestException => (400, "Invalid request"),
            _ => (0, string.Empty)
        };
        if (status == 0) return false;
        context.Response.StatusCode = status;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status, Title = title, Instance = context.Request.Path,
                Type = $"https://httpstatuses.io/{status}"
            }
        });
        return true;
    }
}
