using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ParcelFlow.Delivery.Infrastructure.Persistence;

namespace ParcelFlow.Delivery.Infrastructure;

internal sealed class PostgresHealthCheck(DeliveryDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await database.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Delivery database is unavailable.");
}

internal sealed class RedisHealthCheck(IDistributedCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await cache.GetAsync("readiness", timeout.Token);
        return HealthCheckResult.Healthy();
    }
}
