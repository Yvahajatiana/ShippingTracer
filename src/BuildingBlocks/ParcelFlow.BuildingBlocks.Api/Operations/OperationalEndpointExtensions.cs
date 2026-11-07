using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ParcelFlow.BuildingBlocks.Api.Operations;

public static class OperationalEndpointExtensions
{
    public const string ManagementPortKey = "Management:Port";

    /// <summary>
    /// Maps <c>/health/live</c> (process only) and <c>/health/ready</c> (all registered checks) in a group the caller
    /// extends with the other operational endpoints, such as the Prometheus scraping endpoint.
    /// When <c>Management:Port</c> is set, the group only answers on connections accepted on that local port and
    /// returns 404 on any other, so the public port and the ingress in front of it never serve these endpoints.
    /// The local port is used rather than the Host header, which a client controls. The application stops at startup
    /// when the server does not listen on that port (e.g. an <c>ASPNETCORE_URLS</c> override listing only the API).
    /// </summary>
    public static RouteGroupBuilder MapParcelFlowOperationalEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(string.Empty);
        var managementPort = app.Configuration.GetValue<int?>(ManagementPortKey);
        if (managementPort is { } port)
        {
            if (port is < 1 or > 65535)
                throw new InvalidOperationException($"{ManagementPortKey} must be a TCP port between 1 and 65535.");
            app.Lifetime.ApplicationStarted.Register(() => EnsureListening(app, port));
            ((IEndpointConventionBuilder)group).Add(endpoint =>
            {
                var next = endpoint.RequestDelegate
                    ?? throw new InvalidOperationException("Operational endpoints require a request delegate.");
                endpoint.RequestDelegate = context =>
                {
                    if (context.Connection.LocalPort == port) return next(context);
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
                };
            });
        }
        group.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        group.MapHealthChecks("/health/ready");
        return group;
    }

    private static void EnsureListening(WebApplication app, int port)
    {
        // Servers without network addresses (the in-memory test server) have nothing to check.
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses is null || addresses.Count == 0) return;
        if (addresses.Any(address => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Port == port)) return;
        app.Logger.LogCritical(
            "{Key} is {Port} but the server only listens on {Addresses}: add the port to ASPNETCORE_HTTP_PORTS or ASPNETCORE_URLS. Stopping.",
            ManagementPortKey, port, string.Join(", ", addresses));
        Environment.ExitCode = 1;
        app.Lifetime.StopApplication();
    }
}
