using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.Reporting.Application;
using ParcelFlow.Reporting.Domain;
using ParcelFlow.Reporting.Infrastructure.Persistence;
using Polly;
using Xunit;
namespace ParcelFlow.Reporting.IntegrationTests;
[Collection(InfrastructureCollection.Name)]
public sealed class KpiStoreTests(InfrastructureFixture infrastructure)
{
    private static readonly DateOnly From = new(2026, 10, 15);
    private static readonly DateOnly To = new(2026, 10, 18);
    [Fact]
    public async Task Real_Postgres_aggregates_seed_cohort_current_status_and_excludes_returned_durations()
    {
        await using var provider = ConsumerTestHost.Create(infrastructure, "reporting_seed_" + Guid.NewGuid().ToString("N"));
        await provider.InitializeReportingDatabaseAsync(true); await provider.InitializeReportingDatabaseAsync(true);
        using var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        (await db.Facts.CountAsync()).Should().Be(10);
        var result = await scope.ServiceProvider.GetRequiredService<IReportingStore>().GetKpisAsync(From, To, DateTimeOffset.UtcNow, default);
        result.TotalShipments.Should().Be(3); result.DeliveredShipments.Should().Be(1); result.DeliveryRatePercent.Should().Be(33.33m);
        result.AverageDeliveryHours.Should().Be(26m); result.AverageTransitHours.Should().Be(24m);
        result.DeliveryDurationSamples.Should().Be(1); result.TransitDurationSamples.Should().Be(1);
        result.StatusVolumes.Should().HaveCount(8); result.DailyVolumes.Select(x => x.Shipments).Should().Equal(2, 1, 0);
        result.StatusVolumes.Single(x => x.Status == ParcelStatus.Returned).Shipments.Should().Be(1);
    }
    [Fact]
    public async Task Empty_cohort_has_all_zero_days_and_statuses_and_null_rate_and_delays()
    {
        await using var provider = ConsumerTestHost.Create(infrastructure, "reporting_empty_" + Guid.NewGuid().ToString("N"));
        await provider.InitializeReportingDatabaseAsync(); using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IReportingStore>().GetKpisAsync(From, To, DateTimeOffset.UtcNow, default);
        result.TotalShipments.Should().Be(0); result.DeliveryRatePercent.Should().BeNull(); result.AverageDeliveryHours.Should().BeNull();
        result.AverageTransitHours.Should().BeNull(); result.DailyVolumes.Should().HaveCount(3).And.OnlyContain(x => x.Shipments == 0);
        result.StatusVolumes.Should().HaveCount(8).And.OnlyContain(x => x.Shipments == 0);
    }
    [Fact]
    public async Task Cohort_uses_UTC_creation_half_open_boundaries_ignores_missing_created_and_negative_duration()
    {
        await using var provider = ConsumerTestHost.Create(infrastructure, "reporting_bounds_" + Guid.NewGuid().ToString("N"));
        await provider.InitializeReportingDatabaseAsync(); using var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var start = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero); var end = start.AddDays(3);
        void Add(DateTimeOffset? created, DateTimeOffset delivered, DateTimeOffset? pickup = null)
        {
            var id = Guid.NewGuid(); var metrics = new ShipmentMetrics(id, "PF" + id.ToString("N")[..24].ToUpperInvariant());
            metrics.AddFact(new StatusFact(Guid.NewGuid(), Guid.NewGuid(), id, ParcelStatus.Delivered, delivered, null));
            if (created is { } c) metrics.AddFact(new StatusFact(Guid.NewGuid(), Guid.NewGuid(), id, ParcelStatus.Created, c, null));
            if (pickup is { } p) metrics.AddFact(new StatusFact(Guid.NewGuid(), Guid.NewGuid(), id, ParcelStatus.PickedUp, p, null));
            db.Shipments.Add(metrics);
        }
        Add(start, start.AddHours(2), start.AddHours(1)); Add(end.AddTicks(-10), end.AddHours(1));
        Add(end, end.AddHours(1)); Add(start.AddTicks(-10), start.AddHours(1)); Add(null, start.AddHours(1));
        Add(start.AddHours(1), start, start.AddHours(2)); await db.SaveChangesAsync();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET TIME ZONE 'Europe/Paris'");
        var result = await scope.ServiceProvider.GetRequiredService<IReportingStore>().GetKpisAsync(From, To, DateTimeOffset.UtcNow, default);
        result.TotalShipments.Should().Be(3); result.DeliveredShipments.Should().Be(3); result.DeliveryRatePercent.Should().Be(100);
        result.DeliveryDurationSamples.Should().Be(2); result.TransitDurationSamples.Should().Be(1);
        result.AverageDeliveryHours.Should().Be(1.5m); result.AverageTransitHours.Should().Be(1m);
        result.DailyVolumes.Select(x => x.Shipments).Should().Equal(2, 0, 1);
    }
    [Fact]
    public async Task Real_Redis_keeps_valid_report_for_thirty_seconds_and_rejects_corrupt_or_expired_payload()
    {
        await using var provider = ConsumerTestHost.Create(infrastructure, "reporting_cache_" + Guid.NewGuid().ToString("N"));
        await provider.InitializeReportingDatabaseAsync(true); using var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>(); var store = new ReportingStore(db, ResiliencePipeline.Empty, cache);
        var now = DateTimeOffset.UtcNow; var first = await store.GetKpisAsync(From, To, now, default); var key = store.GetCacheKey(From, To);
        var second = await store.GetKpisAsync(From, To, now.AddSeconds(1), default); second.GeneratedAtUtc.Should().Be(first.GeneratedAtUtc);
        using var redis = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(infrastructure.Redis.GetConnectionString());
        var ttl = await redis.GetDatabase().KeyTimeToLiveAsync("parcelflow:" + key); ttl.Should().NotBeNull(); ttl!.Value.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
        await cache.SetStringAsync(key, "{}"); (await store.GetKpisAsync(From, To, now.AddSeconds(2), default)).TotalShipments.Should().Be(3);
        await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(first with { GeneratedAtUtc = now.AddSeconds(-31) }));
        (await store.GetKpisAsync(From, To, now.AddSeconds(3), default)).GeneratedAtUtc.Should().Be(now.AddSeconds(3));
        await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(first with { DailyVolumes = null! }));
        (await store.GetKpisAsync(From, To, now.AddSeconds(4), default)).DailyVolumes.Should().NotBeNull().And.HaveCount(3);
        await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(first with { GeneratedAtUtc = now.AddHours(1) }));
        (await store.GetKpisAsync(From, To, now.AddSeconds(5), default)).GeneratedAtUtc.Should().Be(now.AddSeconds(5));
    }
    [Fact]
    public async Task Unavailable_cache_falls_back_to_SQL_with_bounded_wait()
    {
        await using var provider = ConsumerTestHost.Create(infrastructure, "reporting_nocache_" + Guid.NewGuid().ToString("N"));
        await provider.InitializeReportingDatabaseAsync(true); using var scope = provider.CreateScope();
        var store = new ReportingStore(scope.ServiceProvider.GetRequiredService<ReportingDbContext>(), ResiliencePipeline.Empty, new SlowCache());
        var watch = System.Diagnostics.Stopwatch.StartNew(); var result = await store.GetKpisAsync(From, To, DateTimeOffset.UtcNow, default);
        result.TotalShipments.Should().Be(3); watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }
    private sealed class SlowCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new TimeoutException();
        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default) { await Task.Delay(TimeSpan.FromSeconds(2), token); return null; }
        public void Refresh(string key) => throw new TimeoutException();
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.FromException(new TimeoutException());
        public void Remove(string key) => throw new TimeoutException();
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.FromException(new TimeoutException());
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new TimeoutException();
        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => await Task.Delay(TimeSpan.FromSeconds(2), token);
    }
}
