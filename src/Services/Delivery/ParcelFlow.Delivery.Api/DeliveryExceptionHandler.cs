using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ParcelFlow.Delivery.Application;

namespace ParcelFlow.Delivery.Api;

public sealed class DeliveryExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            DeliveryNotFoundException => (404, "Resource not found"),
            DeliveryConflictException => (409, "Resource conflict"),
            DeliveryForbiddenException => (403, "Access denied"),
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
