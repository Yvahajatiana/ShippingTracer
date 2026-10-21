using Microsoft.EntityFrameworkCore;
using ParcelFlow.Notification.Application;
using ParcelFlow.Notification.Domain;
using Polly;

namespace ParcelFlow.Notification.Infrastructure.Persistence;

public sealed class NotificationStore(NotificationDbContext database, ResiliencePipeline databaseReadPipeline) : INotificationStore
{
    public async Task LockEventAsync(Guid id, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("Notification mutations require a transaction.");
        var key = "notification:event:" + id;
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }
    private async Task<T> Read<T>(Func<CancellationToken, Task<T>> read, CancellationToken token) =>
        database.Database.CurrentTransaction is not null ? await read(token) : await databaseReadPipeline.ExecuteAsync(async ct => await read(ct), token);
    public Task<NotificationEvent?> FindEventAsync(Guid id, CancellationToken token) => Read(ct =>
        (database.Database.CurrentTransaction is null ? database.Events.AsNoTracking() : database.Events).Include(x => x.Messages).SingleOrDefaultAsync(x => x.EventId == id, ct), token);
    public void AddEvent(NotificationEvent item) => database.Events.Add(item);
    public async Task SaveChangesAsync(CancellationToken token) => await database.SaveChangesAsync(token);
    public Task<NotificationMessage?> FindNotificationAsync(Guid id, CancellationToken token) => Read(ct => database.Messages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct), token);
    public Task<NotificationPageDto> ListNotificationsAsync(string? ownerSubject, Guid? shipmentId, int page, int pageSize, CancellationToken token) => Read(async ct =>
    {
        var query = database.Messages.AsNoTracking();
        if (ownerSubject is not null) query = query.Where(x => x.CustomerSubject == ownerSubject);
        if (shipmentId.HasValue) query = query.Where(x => x.ShipmentId == shipmentId.Value);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.RecordedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new NotificationPageDto(page, pageSize, total, items.Select(x => x.ToDto()).ToArray());
    }, token);
}
