using Microsoft.AspNetCore.Diagnostics;
using ParcelFlow.BuildingBlocks.Api.Errors;
using ParcelFlow.Customer.Application;

namespace ParcelFlow.Customer.Api;

// Maps Customer business exceptions only; malformed requests, domain guards and open circuits
// fall through to the shared ApiExceptionHandler.
public sealed class CustomerExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            CustomerNotFoundException => (404, "Resource not found"),
            CustomerConflictException => (409, "Resource conflict"),
            CustomerForbiddenException => (403, "Access denied"),
            _ => (0, string.Empty)
        };
        if (status == 0) return false;
        await problemDetailsService.WriteProblemAsync(context, status, title);
        return true;
    }
}
