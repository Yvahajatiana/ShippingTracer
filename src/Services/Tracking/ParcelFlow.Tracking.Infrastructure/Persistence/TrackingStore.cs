using Microsoft.EntityFrameworkCore;
using ParcelFlow.Tracking.Application;
using ParcelFlow.Tracking.Domain;
using Polly;

namespace ParcelFlow.Tracking.Infrastructure.Persistence;

public sealed class TrackingStore(TrackingDbContext database, ResiliencePipeline databaseReadPipeline,
    TrackingCacheInvalidator cacheInvalidator) : ITrackingStore
{
    public async Task<TrackingRecord?> FindByShipmentAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        // The consumer outbox owns this transaction. Lock even an absent aggregate so concurrent
        // creation and updates across the three queues cannot exhaust optimistic retries.
        if (database.Database.CurrentTransaction is not null)
        {
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({shipmentId.ToString()}, 0))", cancellationToken);
            // PostgreSQL aborts the whole transaction on error, so an in-transaction retry cannot succeed:
            // fail fast and let the MassTransit message retry replay the consumer in a fresh transaction.
            return await LoadByShipmentAsync(shipmentId, cancellationToken);
        }
        return await databaseReadPipeline.ExecuteAsync(
            async token => await LoadByShipmentAsync(shipmentId, token), cancellationToken);
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
            .Select(value => value.Entity.TrackingNumber).OfType<string>().Distinct().ToArray();
        await database.SaveChangesAsync(cancellationToken);
        // Inside the outbox transaction the removal waits for the commit; removing it now would let a concurrent
        // read put the pre-commit projection back in Redis for the whole cache lifetime.
        await cacheInvalidator.InvalidateAfterCommitAsync(database, numbers, cancellationToken);
    }

    private Task<TrackingRecord?> LoadByShipmentAsync(Guid shipmentId, CancellationToken cancellationToken) =>
        database.Records.Include(value => value.Entries).SingleOrDefaultAsync(value => value.ShipmentId == shipmentId, cancellationToken);
}
