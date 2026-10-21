using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ParcelFlow.Billing.Infrastructure.Persistence;

namespace ParcelFlow.Billing.Infrastructure;

internal sealed class PostgresHealthCheck(BillingDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await database.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Billing database is unavailable.");
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
