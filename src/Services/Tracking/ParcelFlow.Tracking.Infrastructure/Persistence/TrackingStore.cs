using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using ParcelFlow.Tracking.Application;
using ParcelFlow.Tracking.Domain;
using Polly;

namespace ParcelFlow.Tracking.Infrastructure.Persistence;

public sealed class TrackingStore(TrackingDbContext database, ResiliencePipeline databaseReadPipeline,
    IDistributedCache cache, ILogger<TrackingStore> logger) : ITrackingStore
{
    public async Task<TrackingRecord?> FindByShipmentAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        // The consumer outbox owns this transaction. Lock even an absent aggregate so concurrent
        // creation and updates across the three queues cannot exhaust optimistic retries.
        if (database.Database.CurrentTransaction is not null)
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({shipmentId.ToString()}, 0))", cancellationToken);
        return await databaseReadPipeline.ExecuteAsync(async token => await database.Records.Include(value => value.Entries)
            .SingleOrDefaultAsync(value => value.ShipmentId == shipmentId, token), cancellationToken);
    }

    public async Task<TrackingRecord?> FindByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken) =>
        await databaseReadPipeline.ExecuteAsync(async token => await database.Records.AsNoTracking().Include(value => value.Entries)
            .SingleOrDefaultAsync(value => value.TrackingNumber == trackingNumber, token), cancellationToken);

    public void Add(TrackingRecord record) => database.Records.Add(record);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        database.ChangeTracker.DetectChanges();
        var numbers = database.ChangeTracker.Entries<TrackingRecord>()
            .Where(value => value.State is EntityState.Added or EntityState.Modified)
            .Select(value => value.Entity.TrackingNumber).Where(value => value is not null).Distinct().ToArray();
        await database.SaveChangesAsync(cancellationToken);
        foreach (var number in numbers)
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            try
            {
                await cache.RemoveAsync($"tracking:public:v1:{number}", budget.Token).WaitAsync(budget.Token);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                logger.LogWarning(exception, "Tracking cache invalidation failed; cached data expires within 15 seconds");
            }
        }
    }
}
