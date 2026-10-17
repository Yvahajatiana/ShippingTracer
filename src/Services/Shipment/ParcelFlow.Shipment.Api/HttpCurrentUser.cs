using ParcelFlow.Shipment.Application;

namespace ParcelFlow.Shipment.Api;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Subject => accessor.HttpContext?.User.FindFirst("sub")?.Value
        ?? throw new ShipmentForbiddenException();

    public bool IsInRole(string role) => accessor.HttpContext?.User.IsInRole(role) == true;
}
