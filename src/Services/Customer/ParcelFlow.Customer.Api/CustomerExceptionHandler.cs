using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ParcelFlow.Customer.Application;

namespace ParcelFlow.Customer.Api;

public sealed class CustomerExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            CustomerNotFoundException => (404, "Resource not found"),
            CustomerConflictException => (409, "Resource conflict"),
            CustomerForbiddenException => (403, "Access denied"),
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
