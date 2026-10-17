using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Npgsql;
using ParcelFlow.Shipment.Application;
using ParcelFlow.Shipment.Domain;
using Polly;

namespace ParcelFlow.Shipment.Infrastructure.Persistence;

public sealed class ShipmentStore(ShipmentDbContext database, ResiliencePipeline databaseReadPipeline,
    IDistributedCache cache, ILogger<ShipmentStore> logger) : IShipmentStore
{
    private const string TariffCacheKey = "shipment:tariffs:v1:all";
    public async Task<ParcelShipment?> FindShipmentAsync(Guid id, CancellationToken cancellationToken) =>
        await databaseReadPipeline.ExecuteAsync(async token =>
            await database.Shipments.SingleOrDefaultAsync(value => value.Id == id, token), cancellationToken);

    public async Task<IReadOnlyList<ParcelShipment>> ListShipmentsAsync(string? ownerSubject, int offset, int limit,
        CancellationToken cancellationToken) => await databaseReadPipeline.ExecuteAsync(async token =>
            await database.Shipments.AsNoTracking().Where(value => ownerSubject == null || value.OwnerSubject == ownerSubject)
                .OrderByDescending(value => value.CreatedAtUtc).ThenBy(value => value.Id)
                .Skip(offset).Take(limit).ToListAsync(token), cancellationToken);

    public async Task<Tariff?> FindTariffAsync(DeliveryZone zone, CancellationToken cancellationToken) =>
        await databaseReadPipeline.ExecuteAsync(async token =>
            await database.Tariffs.SingleOrDefaultAsync(value => value.Zone == zone, token), cancellationToken);

    public async Task<IReadOnlyList<Tariff>> ListTariffsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(350));
            var json = await cache.GetStringAsync(TariffCacheKey, timeout.Token);
            if (json is not null && JsonSerializer.Deserialize<TariffCacheItem[]>(json) is { } items)
                return items.Select(value => new Tariff(value.Zone, value.BasePrice, value.PricePerKg)).ToArray();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is not OutOfMemoryException)
        {
            logger.LogWarning(exception, "Tariff cache read failed; reading PostgreSQL");
        }
        var tariffs = await databaseReadPipeline.ExecuteAsync(async token =>
            await database.Tariffs.AsNoTracking().OrderBy(value => value.Zone).ToListAsync(token), cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(350));
            await cache.SetStringAsync(TariffCacheKey, JsonSerializer.Serialize(tariffs.Select(value =>
                    new TariffCacheItem(value.Zone, value.BasePrice, value.PricePerKg))),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) }, timeout.Token);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is not OutOfMemoryException)
        {
            logger.LogWarning(exception, "Tariff cache write failed");
        }
        return tariffs;
    }

    public void AddShipment(ParcelShipment shipment) => database.Shipments.Add(shipment);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        database.ChangeTracker.DetectChanges();
        var tariffsChanged = database.ChangeTracker.Entries<Tariff>()
            .Any(value => value.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ShipmentConflictException("The shipment was modified concurrently. Reload and retry.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ShipmentConflictException("This tracking number already exists. Retry creation.");
        }
        if (tariffsChanged)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
            try
            {
                await cache.RemoveAsync(TariffCacheKey, timeout.Token);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                logger.LogWarning(exception, "Tariff cache invalidation failed; entries expire within 60 seconds");
            }
        }
    }

    private sealed record TariffCacheItem(DeliveryZone Zone, decimal BasePrice, decimal PricePerKg);
}
