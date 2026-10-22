using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace ParcelFlow.Tracking.Infrastructure.Persistence;

/// <summary>
/// Defers public projection cache invalidation until the database transaction that wrote the projection commits.
/// Writes performed inside an explicit transaction (the MassTransit consumer outbox) only enqueue keys; the keys are
/// removed from Redis once EF Core reports the commit and discarded when the transaction rolls back or fails.
/// </summary>
public sealed class TrackingCacheInvalidator(IDistributedCache cache, ILogger<TrackingCacheInvalidator> logger)
    : DbTransactionInterceptor
{
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(250);
    private readonly ConditionalWeakTable<DbContext, HashSet<string>> _pending = new();

    public static string PublicKey(string trackingNumber) => $"tracking:public:v1:{trackingNumber}";

    /// <summary>
    /// Invalidates the public projections of <paramref name="trackingNumbers"/> once their write is durable:
    /// immediately when no transaction is open (the write is already committed), otherwise after the commit.
    /// </summary>
    public Task InvalidateAfterCommitAsync(DbContext context, IReadOnlyCollection<string> trackingNumbers,
        CancellationToken cancellationToken)
    {
        if (trackingNumbers.Count == 0) return Task.CompletedTask;
        if (context.Database.CurrentTransaction is null) return InvalidateAsync(trackingNumbers, cancellationToken);
        var keys = _pending.GetOrCreateValue(context);
        lock (keys) keys.UnionWith(trackingNumbers);
        return Task.CompletedTask;
    }

    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        // The commit is durable at this point: never let the caller's token or Redis abort the flush.
        var numbers = Take(eventData.Context);
        if (numbers.Length > 0) await InvalidateAsync(numbers, CancellationToken.None);
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        var numbers = Take(eventData.Context);
        if (numbers.Length > 0) InvalidateAsync(numbers, CancellationToken.None).GetAwaiter().GetResult();
    }

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Take(eventData.Context);
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        Take(eventData.Context);

    public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Discard(eventData);
        return Task.CompletedTask;
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) => Discard(eventData);

    public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
        DbTransaction result, CancellationToken cancellationToken = default)
    {
        // A transaction disposed without an explicit commit or rollback raises no end event; start from a clean slate.
        Take(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        Take(eventData.Context);
        return result;
    }

    private void Discard(TransactionErrorEventData eventData)
    {
        // Savepoint failures leave the enclosing transaction alive; only a failed commit or rollback ends it.
        if (eventData.Action is "Commit" or "Rollback") Take(eventData.Context);
    }

    private string[] Take(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var keys)) return [];
        lock (keys)
        {
            var numbers = keys.ToArray();
            keys.Clear();
            return numbers;
        }
    }

    private async Task InvalidateAsync(IEnumerable<string> trackingNumbers, CancellationToken cancellationToken)
    {
        foreach (var number in trackingNumbers)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(Budget);
            try
            {
                await cache.RemoveAsync(PublicKey(number), budget.Token).WaitAsync(budget.Token);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The write is already committed: a cache outage only delays freshness until the entry expires.
                logger.LogWarning(exception, "Tracking cache invalidation failed; cached data expires within 15 seconds");
            }
        }
    }
}
