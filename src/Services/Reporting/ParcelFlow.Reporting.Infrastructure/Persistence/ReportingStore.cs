using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using ParcelFlow.Reporting.Application;
using ParcelFlow.Reporting.Domain;
using Polly;
namespace ParcelFlow.Reporting.Infrastructure.Persistence;
public sealed class ReportingStore(ReportingDbContext database, ResiliencePipeline databaseReadPipeline, IDistributedCache cache) : IReportingStore
{
    public async Task LockAsync(Guid eventId, Guid sourceEventId, Guid shipmentId, CancellationToken token)
    {
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("Reporting mutations require a transaction.");
        foreach (var key in new[] { "reporting:event:" + eventId, "reporting:source:" + sourceEventId, "reporting:shipment:" + shipmentId })
            await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", token);
    }
    public Task<StatusFact?> FindFactByEventIdAsync(Guid id, CancellationToken token) => database.Facts.SingleOrDefaultAsync(x => x.EventId == id, token);
    public Task<StatusFact?> FindFactBySourceIdAsync(Guid id, CancellationToken token) => database.Facts.SingleOrDefaultAsync(x => x.SourceEventId == id, token);
    public Task<ShipmentMetrics?> FindShipmentAsync(Guid id, CancellationToken token) => database.Shipments.Include(x => x.Facts).SingleOrDefaultAsync(x => x.ShipmentId == id, token);
    public void AddShipment(ShipmentMetrics shipment) => database.Shipments.Add(shipment);
    public async Task SaveChangesAsync(CancellationToken token) => await database.SaveChangesAsync(token);
    public string GetCacheKey(DateOnly from, DateOnly to)
    {
        var connection = database.Database.GetDbConnection();
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connection.DataSource + "|" + connection.Database)));
        return $"reporting:kpis:v1:{identity}:{from:yyyyMMdd}:{to:yyyyMMdd}";
    }
    public async Task<KpiReportDto> GetKpisAsync(DateOnly from, DateOnly to, DateTimeOffset generatedAtUtc, CancellationToken token)
    {
        var key = GetCacheKey(from, to);
        try
        {
            var bytes = await cache.GetAsync(key, token).WaitAsync(TimeSpan.FromMilliseconds(250), token);
            if (bytes is { Length: > 0 and <= 131072 })
            {
                var cached = JsonSerializer.Deserialize<KpiReportDto>(bytes);
                if (cached is not null && Valid(cached, from, to, generatedAtUtc)) return cached;
            }
        }
        catch (Exception exception) when (!token.IsCancellationRequested && exception is not OutOfMemoryException) { }
        var result = await databaseReadPipeline.ExecuteAsync(async ct =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            await database.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            var end = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            var cohort = database.Shipments.AsNoTracking().Where(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end);
            var summary = await cohort.GroupBy(_ => 1).Select(group => new
            {
                Total = group.LongCount(), Delivered = group.LongCount(x => x.CurrentStatus == ParcelStatus.Delivered),
                DeliverySamples = group.LongCount(x => x.CurrentStatus == ParcelStatus.Delivered && x.DeliverySeconds != null),
                TransitSamples = group.LongCount(x => x.CurrentStatus == ParcelStatus.Delivered && x.TransitSeconds != null),
                DeliveryAverage = group.Average(x => x.CurrentStatus == ParcelStatus.Delivered ? x.DeliverySeconds : null),
                TransitAverage = group.Average(x => x.CurrentStatus == ParcelStatus.Delivered ? x.TransitSeconds : null)
            }).SingleOrDefaultAsync(ct);
            var statuses = await cohort.GroupBy(x => x.CurrentStatus).Select(group => new { Status = group.Key, Count = group.LongCount() }).ToListAsync(ct);
            var daily = await cohort.GroupBy(x => new { x.CreatedAtUtc!.Value.Year, x.CreatedAtUtc.Value.Month, x.CreatedAtUtc.Value.Day })
                .Select(group => new { group.Key.Year, group.Key.Month, group.Key.Day, Count = group.LongCount() }).ToListAsync(ct);
            await transaction.CommitAsync(ct);
            var volumes = Enumerable.Range(0, to.DayNumber - from.DayNumber).Select(offset =>
            {
                var date = from.AddDays(offset);
                return new DailyVolumeDto(date, daily.SingleOrDefault(x => new DateOnly(x.Year, x.Month, x.Day) == date)?.Count ?? 0);
            }).ToArray();
            return new KpiReportDto(from, to, generatedAtUtc.ToUniversalTime(), summary?.Total ?? 0, summary?.Delivered ?? 0,
                summary is { Total: > 0 } ? Round(100m * summary.Delivered / summary.Total) : null,
                Hours(summary?.DeliveryAverage), summary?.DeliverySamples ?? 0, Hours(summary?.TransitAverage), summary?.TransitSamples ?? 0,
                volumes, Enum.GetValues<ParcelStatus>().Select(status => new StatusVolumeDto(status, statuses.SingleOrDefault(x => x.Status == status)?.Count ?? 0)).ToArray());
        }, token);
        try
        {
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(result), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) }, token)
                .WaitAsync(TimeSpan.FromMilliseconds(250), token);
        }
        catch (Exception exception) when (!token.IsCancellationRequested && exception is not OutOfMemoryException) { }
        return result;
    }
    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal? Hours(decimal? seconds) => seconds.HasValue ? Round(seconds.Value / 3600m) : null;
    private static bool Valid(KpiReportDto value, DateOnly from, DateOnly to, DateTimeOffset now)
    {
        if (value.From != from || value.To != to || value.GeneratedAtUtc.Offset != TimeSpan.Zero || value.GeneratedAtUtc > now || now - value.GeneratedAtUtc > TimeSpan.FromSeconds(30)
            || value.TotalShipments < 0 || value.DeliveredShipments < 0 || value.DeliveredShipments > value.TotalShipments
            || value.DailyVolumes is null || value.StatusVolumes is null || value.DailyVolumes.Count != to.DayNumber - from.DayNumber || value.StatusVolumes.Count != 8) return false;
        var statuses = Enum.GetValues<ParcelStatus>();
        if (!value.StatusVolumes.Select(x => x.Status).SequenceEqual(statuses) || value.StatusVolumes.Any(x => x.Shipments < 0 || x.Shipments > value.TotalShipments)
            || value.StatusVolumes.Sum(x => (decimal)x.Shipments) != value.TotalShipments || value.StatusVolumes.Single(x => x.Status == ParcelStatus.Delivered).Shipments != value.DeliveredShipments) return false;
        if (value.DailyVolumes.Where((day, index) => day.Date != from.AddDays(index) || day.Shipments < 0 || day.Shipments > value.TotalShipments).Any()
            || value.DailyVolumes.Sum(x => (decimal)x.Shipments) != value.TotalShipments) return false;
        if (value.DeliveryRatePercent != (value.TotalShipments == 0 ? null : Round(100m * value.DeliveredShipments / value.TotalShipments))) return false;
        return DurationValid(value.DeliveryDurationSamples, value.AverageDeliveryHours, value.DeliveredShipments)
            && DurationValid(value.TransitDurationSamples, value.AverageTransitHours, value.DeliveredShipments);
    }
    private static bool DurationValid(long samples, decimal? average, long delivered) => samples >= 0 && samples <= delivered
        && (samples == 0 ? average is null : average is >= 0);
}
