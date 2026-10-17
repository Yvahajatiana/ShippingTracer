using Microsoft.EntityFrameworkCore;
using ParcelFlow.Warehouse.Application;
using ParcelFlow.Warehouse.Domain;
using Polly;

namespace ParcelFlow.Warehouse.Infrastructure.Persistence;

public sealed class WarehouseStore(WarehouseDbContext database, ResiliencePipeline databaseReadPipeline) : IWarehouseStore
{
    public async Task LockParcelAsync(string trackingNumber, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Parcel mutations require a transaction.");
        var key = "warehouse:parcel:" + trackingNumber;
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    public async Task<WarehouseSite?> FindWarehouseAsync(Guid id, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is not null)
        {
            var key = "warehouse:site:" + id;
            await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        }
        if (database.Database.CurrentTransaction is not null)
            return await database.Warehouses.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return await databaseReadPipeline.ExecuteAsync(async token => await database.Warehouses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token), cancellationToken);
    }
    public Task<ParcelInventory?> FindInventoryAsync(string trackingNumber, CancellationToken cancellationToken) =>
        database.Inventory.SingleOrDefaultAsync(x => x.TrackingNumber == trackingNumber, cancellationToken);
    public Task<ParcelInventory?> FindInventoryByShipmentAsync(Guid shipmentId, CancellationToken cancellationToken) =>
        database.Inventory.SingleOrDefaultAsync(x => x.ShipmentId == shipmentId, cancellationToken);
    public Task<WarehouseScan?> FindScanAsync(Guid scanId, CancellationToken cancellationToken) =>
        database.Scans.AsNoTracking().SingleOrDefaultAsync(x => x.ScanId == scanId, cancellationToken);
    public void AddWarehouse(WarehouseSite warehouse) => database.Warehouses.Add(warehouse);
    public void AddInventory(ParcelInventory inventory) => database.Inventory.Add(inventory);
    public void AddScan(WarehouseScan scan) => database.Scans.Add(scan);
    public async Task SaveChangesAsync(CancellationToken cancellationToken) => await database.SaveChangesAsync(cancellationToken);
    public Task<bool> HasStockAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        database.Inventory.AnyAsync(x => x.CurrentWarehouseId == warehouseId, cancellationToken);

    public async Task<WarehousePageDto> ListWarehousesAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        await databaseReadPipeline.ExecuteAsync(async token =>
        {
            var query = database.Warehouses.AsNoTracking();
            var total = await query.CountAsync(token);
            var items = await query.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(token);
            return new WarehousePageDto(page, pageSize, total, items.Select(x => x.ToDto()).ToArray());
        }, cancellationToken);

    public async Task<WarehouseStockDto> GetStockAsync(Guid warehouseId, int page, int pageSize, CancellationToken cancellationToken) =>
        await databaseReadPipeline.ExecuteAsync(async token =>
        {
            var query = database.Inventory.AsNoTracking().Where(x => x.CurrentWarehouseId == warehouseId);
            var total = await query.CountAsync(token);
            var items = await query.OrderBy(x => x.TrackingNumber).ThenBy(x => x.ShipmentId)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new StockParcelDto(x.ShipmentId, x.TrackingNumber, x.LastScanAtUtc!.Value)).ToListAsync(token);
            return new WarehouseStockDto(warehouseId, page, pageSize, total, items);
        }, cancellationToken);
}
