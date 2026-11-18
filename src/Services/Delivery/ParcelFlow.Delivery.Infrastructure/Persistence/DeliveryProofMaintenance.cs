using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParcelFlow.Delivery.Domain;

namespace ParcelFlow.Delivery.Infrastructure.Persistence;

public sealed record DeliveryProofMaintenanceResult(int Purged, int Encrypted);

/// <summary>
/// GDPR maintenance of the delivery proofs: erases the recipient name and the proof images once the
/// retention period is over (the delivery outcome and its date stay), then encrypts the proofs still
/// stored in plaintext by versions older than the encryption. Safe to run on several replicas at once,
/// even with different retention settings: both writes re-check that the row is not purged yet, and
/// encrypting a value twice stays readable.
/// </summary>
public sealed class DeliveryProofMaintenance(DeliveryDbContext database, DeliveryProofSettings settings, TimeProvider clock)
{
    private const int BatchSize = 200;

    public async Task<DeliveryProofMaintenanceResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = DeliveryRules.NormalizeTimestamp(clock.GetUtcNow());
        var cutoff = now.AddDays(-settings.RetentionDays);
        var purged = 0;
        while (true)
        {
            var batch = await database.Attempts.AsNoTracking()
                .Where(x => x.ProofsPurgedAtUtc == null && x.Outcome == DeliveryOutcome.Delivered && x.OccurredAtUtc < cutoff)
                .OrderBy(x => x.OccurredAtUtc).Select(x => x.OperationId).Take(BatchSize).ToListAsync(cancellationToken);
            if (batch.Count == 0) break;
            purged += await database.Attempts
                .Where(x => batch.Contains(x.OperationId) && x.ProofsPurgedAtUtc == null)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.RecipientName, (string?)null)
                    .SetProperty(x => x.SignaturePng, (byte[]?)null)
                    .SetProperty(x => x.Photo, (byte[]?)null)
                    .SetProperty(x => x.PhotoContentType, (string?)null)
                    .SetProperty(x => x.ProofsPurgedAtUtc, now), cancellationToken);
            if (batch.Count < BatchSize) break;
        }

        var encrypted = new HashSet<Guid>();
        var marker = DeliveryProofProtector.FormatMarker.ToArray();
        while (true)
        {
            var batch = await database.Database.SqlQuery<Guid>($"""
                SELECT "OperationId" AS "Value" FROM delivery_attempts
                WHERE ("RecipientName" IS NOT NULL AND substring("RecipientName" FROM 1 FOR {marker.Length}) <> {marker})
                   OR ("SignaturePng" IS NOT NULL AND substring("SignaturePng" FROM 1 FOR {marker.Length}) <> {marker})
                   OR ("Photo" IS NOT NULL AND substring("Photo" FROM 1 FOR {marker.Length}) <> {marker})
                LIMIT {BatchSize}
                """).ToListAsync(cancellationToken);
            if (batch.Count == 0) break;
            // A row selected twice was not encrypted by the previous batch: stop instead of looping.
            if (batch.Any(encrypted.Contains)) throw new InvalidOperationException("Delivery proofs encryption made no progress.");
            var attempts = await database.Attempts.AsNoTracking().Where(x => batch.Contains(x.OperationId)).ToListAsync(cancellationToken);
            foreach (var attempt in attempts)
            {
                // The converters returned the legacy plaintext unchanged: writing it back encrypts it.
                // Conditional write: another replica may have purged the row since it was read, and
                // the proofs must never come back on a purged row.
                await database.Attempts
                    .Where(x => x.OperationId == attempt.OperationId && x.ProofsPurgedAtUtc == null)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(x => x.RecipientName, attempt.RecipientName)
                        .SetProperty(x => x.SignaturePng, attempt.SignaturePng)
                        .SetProperty(x => x.Photo, attempt.Photo), cancellationToken);
            }
            encrypted.UnionWith(batch);
            if (batch.Count < BatchSize) break;
        }
        return new(purged, encrypted.Count);
    }
}

public sealed class DeliveryProofMaintenanceService(IServiceScopeFactory scopes, DeliveryProofSettings settings, TimeProvider clock,
    ILogger<DeliveryProofMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(settings.PurgeInterval, clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<DeliveryProofMaintenance>().RunAsync(stoppingToken);
                if (result.Purged > 0 || result.Encrypted > 0)
                    logger.LogInformation("Delivery proofs maintenance: {Purged} purged after {RetentionDays} days, {Encrypted} encrypted",
                        result.Purged, settings.RetentionDays, result.Encrypted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The next tick retries: a database outage must not stop the service.
                logger.LogError(exception, "Delivery proofs maintenance failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try { return await timer.WaitForNextTickAsync(stoppingToken); }
        catch (OperationCanceledException) { return false; }
    }
}
