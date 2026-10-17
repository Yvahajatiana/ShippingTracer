using ParcelFlow.Customer.Application;

namespace ParcelFlow.Customer.Api;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Subject => accessor.HttpContext?.User.FindFirst("sub")?.Value
        ?? throw new CustomerForbiddenException();

    public bool IsInRole(string role) => accessor.HttpContext?.User.IsInRole(role) == true;
}
