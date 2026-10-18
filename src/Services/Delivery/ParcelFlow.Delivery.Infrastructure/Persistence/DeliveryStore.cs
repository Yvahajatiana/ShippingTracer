using Microsoft.EntityFrameworkCore;
using ParcelFlow.Delivery.Application;
using ParcelFlow.Delivery.Domain;
using Polly;

namespace ParcelFlow.Delivery.Infrastructure.Persistence;

public sealed class DeliveryStore(DeliveryDbContext database, ResiliencePipeline databaseReadPipeline) : IDeliveryStore
{
    public Task LockRouteAsync(Guid id, CancellationToken cancellationToken) => LockAsync("route:" + id, cancellationToken);
    public Task LockParcelAsync(Guid id, CancellationToken cancellationToken) => LockAsync("parcel:" + id, cancellationToken);
    public Task LockDriverAsync(string subject, CancellationToken cancellationToken) => LockAsync("driver:" + subject, cancellationToken);
    private async Task LockAsync(string identity, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("Delivery mutations require a transaction.");
        var key = "delivery:" + identity;
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }
    private IQueryable<T> Query<T>() where T : class => database.Database.CurrentTransaction is null ? database.Set<T>().AsNoTracking() : database.Set<T>();
    private async Task<T> Read<T>(Func<CancellationToken, Task<T>> read, CancellationToken token) =>
        database.Database.CurrentTransaction is not null ? await read(token) : await databaseReadPipeline.ExecuteAsync(async cancellation => await read(cancellation), token);
    public Task<DeliveryRoute?> FindRouteAsync(Guid id, CancellationToken token) => Read(ct => Query<DeliveryRoute>().Include(x => x.Stops).SingleOrDefaultAsync(x => x.Id == id, ct), token);
    public Task<DeliveryParcel?> FindParcelAsync(Guid id, CancellationToken token) => Read(ct => Query<DeliveryParcel>().SingleOrDefaultAsync(x => x.ShipmentId == id, ct), token);
    public Task<DeliveryDriver?> FindDriverAsync(string subject, CancellationToken token) => Read(ct => Query<DeliveryDriver>().SingleOrDefaultAsync(x => x.Subject == subject, ct), token);
    public Task<DeliveryAttempt?> FindAttemptAsync(Guid operationId, CancellationToken token) => Read(ct => database.Attempts.AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == operationId, ct), token);
    public Task<DeliveryAttempt?> FindAttemptByStopAsync(Guid routeId, Guid shipmentId, CancellationToken token) => Read(ct => database.Attempts.AsNoTracking().SingleOrDefaultAsync(x => x.RouteId == routeId && x.ShipmentId == shipmentId, ct), token);
    public Task<bool> HasOpenRoutesAsync(string subject, CancellationToken token) => Read(ct => database.Routes.AsNoTracking().AnyAsync(x => x.DriverSubject == subject && x.Status != DeliveryRouteStatus.Completed, ct), token);
    public void AddDriver(DeliveryDriver driver) => database.Drivers.Add(driver);
    public void AddRoute(DeliveryRoute route) => database.Routes.Add(route);
    public void AddParcel(DeliveryParcel parcel) => database.Parcels.Add(parcel);
    public void AddAttempt(DeliveryAttempt attempt) => database.Attempts.Add(attempt);
    public async Task SaveChangesAsync(CancellationToken token) => await database.SaveChangesAsync(token);
    public async Task<RoutePageDto> ListRoutesAsync(int page, int pageSize, CancellationToken token) => await Read(async ct =>
    {
        var query = database.Routes.AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query.Include(x => x.Stops).OrderBy(x => x.ServiceDate).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new RoutePageDto(page, pageSize, total, items.Select(x => x.ToDto()).ToArray());
    }, token);
    public async Task<DriverPageDto> ListDriversAsync(int page, int pageSize, CancellationToken token) => await Read(async ct =>
    {
        var query = database.Drivers.AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Subject).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new DriverDto(x.Subject, x.DisplayName, x.IsActive)).ToListAsync(ct);
        return new DriverPageDto(page, pageSize, total, items);
    }, token);
    public async Task<AvailableParcelPageDto> ListAvailableParcelsAsync(int page, int pageSize, CancellationToken token) => await Read(async ct =>
    {
        var query = database.Parcels.AsNoTracking().Where(x => x.TrackingNumber != null && x.IsPickedUp && !x.IsDelivered && x.ActiveRouteId == null);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.TrackingNumber).ThenBy(x => x.ShipmentId).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new AvailableParcelDto(x.ShipmentId, x.TrackingNumber!)).ToListAsync(ct);
        return new AvailableParcelPageDto(page, pageSize, total, items);
    }, token);
}
