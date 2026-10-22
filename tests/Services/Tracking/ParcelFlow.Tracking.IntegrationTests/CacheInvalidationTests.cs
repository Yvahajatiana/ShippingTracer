using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Tracking.Application;
using ParcelFlow.Tracking.Domain;
using ParcelFlow.Tracking.Infrastructure;
using ParcelFlow.Tracking.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Tracking.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class CacheInvalidationTests(InfrastructureFixture infrastructure)
{
    private const string Stale = "stale-projection";

    private IHost CreateHost(IDistributedCache? cache = null) => Host.CreateDefaultBuilder().ConfigureAppConfiguration(configuration =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:TrackingDatabase"] = infrastructure.Postgres.GetConnectionString(),
            ["ConnectionStrings:Redis"] = infrastructure.Redis.GetConnectionString(),
            ["RabbitMq:Host"] = infrastructure.RabbitMq.Hostname,
            ["RabbitMq:Port"] = infrastructure.RabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:Username"] = "rabbitmq", ["RabbitMq:Password"] = "rabbitmq"
        })).ConfigureServices((context, services) =>
        {
            services.AddTrackingApplication().AddTrackingInfrastructure(context.Configuration);
            if (cache is not null) services.AddSingleton(cache);
        }).Build();

    private static string NewNumber() => "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();

    private static async Task SeedStaleCacheAsync(IHost host, string number) =>
        await host.Services.GetRequiredService<IDistributedCache>().SetStringAsync(TrackingCacheInvalidator.PublicKey(number), Stale);

    private static Task<string?> ReadCacheAsync(IHost host, string number) =>
        host.Services.GetRequiredService<IDistributedCache>().GetStringAsync(TrackingCacheInvalidator.PublicKey(number));

    private static TrackingRecord NewRecord(string number)
    {
        var record = new TrackingRecord(Guid.NewGuid());
        record.Register(number);
        record.AddEntry(new TrackingEntry(Guid.NewGuid(), ParcelStatus.Created, DateTimeOffset.UtcNow, null));
        return record;
    }

    [Fact]
    public async Task Transactional_write_invalidates_public_cache_only_after_commit()
    {
        using var host = CreateHost();
        await host.Services.InitializeTrackingDatabaseAsync();
        var number = NewNumber();
        await SeedStaleCacheAsync(host, number);
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<ITrackingStore>();
        await using var transaction = await database.Database.BeginTransactionAsync();
        store.Add(NewRecord(number));
        await store.SaveChangesAsync(CancellationToken.None);

        // A concurrent reader still sees the committed state, so the cached entry must survive until the commit.
        (await ReadCacheAsync(host, number)).Should().Be(Stale);
        await transaction.CommitAsync();
        (await ReadCacheAsync(host, number)).Should().BeNull();
    }

    [Fact]
    public async Task Rolled_back_write_keeps_public_cache_and_does_not_leak_into_next_transaction()
    {
        using var host = CreateHost();
        await host.Services.InitializeTrackingDatabaseAsync();
        var number = NewNumber();
        await SeedStaleCacheAsync(host, number);
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<ITrackingStore>();
        await using (var transaction = await database.Database.BeginTransactionAsync())
        {
            store.Add(NewRecord(number));
            await store.SaveChangesAsync(CancellationToken.None);
            await transaction.RollbackAsync();
        }
        (await ReadCacheAsync(host, number)).Should().Be(Stale);

        // The discarded key must not be flushed by a later, unrelated commit on the same context.
        database.ChangeTracker.Clear();
        await using (var transaction = await database.Database.BeginTransactionAsync())
        {
            store.Add(NewRecord(NewNumber()));
            await store.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync();
        }
        (await ReadCacheAsync(host, number)).Should().Be(Stale);
        (await database.Records.AnyAsync(x => x.TrackingNumber == number)).Should().BeFalse();
    }

    [Fact]
    public async Task Write_without_ambient_transaction_invalidates_public_cache_after_save()
    {
        using var host = CreateHost();
        await host.Services.InitializeTrackingDatabaseAsync();
        var number = NewNumber();
        await SeedStaleCacheAsync(host, number);
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ITrackingStore>();
        store.Add(NewRecord(number));
        await store.SaveChangesAsync(CancellationToken.None);
        (await ReadCacheAsync(host, number)).Should().BeNull();
    }

    [Fact]
    public async Task Caller_cancellation_after_a_committed_save_does_not_skip_invalidation()
    {
        using var host = CreateHost();
        await host.Services.InitializeTrackingDatabaseAsync();
        var number = NewNumber();
        await SeedStaleCacheAsync(host, number);
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<ITrackingStore>();
        using var caller = new CancellationTokenSource();
        // The caller is canceled right after the durable save, before the cache removal runs.
        database.SavedChanges += (_, _) => caller.Cancel();
        store.Add(NewRecord(number));
        await store.SaveChangesAsync(caller.Token);
        caller.IsCancellationRequested.Should().BeTrue();
        (await ReadCacheAsync(host, number)).Should().BeNull();
    }

    [Fact]
    public async Task Cache_outage_during_post_commit_invalidation_keeps_the_committed_write()
    {
        using var host = CreateHost(new UnavailableCache());
        await host.Services.InitializeTrackingDatabaseAsync();
        var number = NewNumber();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<ITrackingStore>();
            await using var transaction = await database.Database.BeginTransactionAsync();
            store.Add(NewRecord(number));
            await store.SaveChangesAsync(CancellationToken.None);
            await FluentActions.Awaiting(() => transaction.CommitAsync()).Should().NotThrowAsync();
        }
        await using var verification = host.Services.CreateAsyncScope();
        (await verification.ServiceProvider.GetRequiredService<TrackingDbContext>().Records
            .AnyAsync(x => x.TrackingNumber == number)).Should().BeTrue();
    }

    [Fact]
    public async Task Consumed_fact_invalidates_public_cache_once_the_outbox_transaction_commits()
    {
        using var host = CreateHost();
        await host.Services.InitializeTrackingDatabaseAsync();
        await host.StartAsync();
        try
        {
            var shipment = Guid.NewGuid();
            var number = NewNumber();
            await SeedStaleCacheAsync(host, number);
            await host.Services.GetRequiredService<IBus>().Publish(new ParcelStatusRecorded(Guid.NewGuid(), shipment,
                DateTimeOffset.UtcNow, shipment, number, TrackingStatus.InTransit, "FR-LYON-01"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (await ReadCacheAsync(host, number) is not null) await Task.Delay(100, timeout.Token);
            await using var scope = host.Services.CreateAsyncScope();
            (await scope.ServiceProvider.GetRequiredService<TrackingDbContext>().Records
                .AnyAsync(x => x.ShipmentId == shipment)).Should().BeTrue();
        }
        finally { await host.StopAsync(); }
    }

    private sealed class UnavailableCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("Redis unavailable.");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("Redis unavailable.");
        public void Refresh(string key) => throw new InvalidOperationException("Redis unavailable.");
        public Task RefreshAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("Redis unavailable.");
        public void Remove(string key) => throw new InvalidOperationException("Redis unavailable.");
        public Task RemoveAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("Redis unavailable.");
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new InvalidOperationException("Redis unavailable.");
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            throw new InvalidOperationException("Redis unavailable.");
    }
}
