using Microsoft.AspNetCore.Diagnostics;
using ParcelFlow.BuildingBlocks.Api.Errors;
using ParcelFlow.Warehouse.Application;

namespace ParcelFlow.Warehouse.Api;

// Maps Warehouse business exceptions only; malformed requests, domain guards and open circuits
// fall through to the shared ApiExceptionHandler.
public sealed class WarehouseExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            WarehouseNotFoundException => (404, "Resource not found"),
            WarehouseConflictException => (409, "Resource conflict"),
            _ => (0, string.Empty)
        };
        if (status == 0) return false;
        await problemDetailsService.WriteProblemAsync(context, status, title);
        return true;
    }
}
