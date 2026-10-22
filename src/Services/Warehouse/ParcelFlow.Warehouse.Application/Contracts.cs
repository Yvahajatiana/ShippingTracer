using MediatR;
using ParcelFlow.Warehouse.Domain;

namespace ParcelFlow.Warehouse.Application;

public interface IWarehouseStore
{
    Task LockParcelAsync(string trackingNumber, CancellationToken cancellationToken);
    Task<WarehouseSite?> FindWarehouseAsync(Guid id, CancellationToken cancellationToken);
    Task<ParcelInventory?> FindInventoryAsync(string trackingNumber, CancellationToken cancellationToken);
    Task<ParcelInventory?> FindInventoryByShipmentAsync(Guid shipmentId, CancellationToken cancellationToken);
    Task<WarehouseScan?> FindScanAsync(Guid scanId, CancellationToken cancellationToken);
    void AddWarehouse(WarehouseSite warehouse);
    void AddInventory(ParcelInventory inventory);
    void AddScan(WarehouseScan scan);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<WarehousePageDto> ListWarehousesAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<WarehouseStockDto> GetStockAsync(Guid warehouseId, int page, int pageSize, CancellationToken cancellationToken);
    Task<bool> HasStockAsync(Guid warehouseId, CancellationToken cancellationToken);
}
public interface IWarehouseCommand { }
public sealed record CreateWarehouse(Guid Id, string Code, string Name, WarehouseKind Kind, string Street, string City, string PostalCode, string CountryCode) : IRequest<WarehouseDto>, IWarehouseCommand;
public sealed record UpdateWarehouse(Guid Id, string Name, string Street, string City, string PostalCode, string CountryCode, bool IsActive) : IRequest<WarehouseDto>, IWarehouseCommand;
public sealed record ScanParcel(Guid ScanId, Guid WarehouseId, string TrackingNumber, ScanDirection Direction, string OperatorSubject) : IRequest<ScanDto>, IWarehouseCommand;
public sealed record RegisterReadyShipment(Guid ShipmentId, string TrackingNumber) : IRequest<bool>, IWarehouseCommand;
/// <summary>Records the automatic stock exit of a parcel whose delivery has started, if it is still in stock.</summary>
public sealed record RecordDeliveryExit(Guid SourceEventId, Guid ShipmentId, string TrackingNumber, DateTimeOffset OccurredAtUtc) : IRequest<DeliveryExitOutcome>, IWarehouseCommand;
public enum DeliveryExitOutcome { Recorded = 0, AlreadyRecorded = 1, NotInStock = 2, UnknownParcel = 3, Superseded = 4 }
public sealed record GetWarehouse(Guid Id) : IRequest<WarehouseDto>;
public sealed record ListWarehouses(int Page = 1, int PageSize = 50) : IRequest<WarehousePageDto>;
public sealed record GetWarehouseStock(Guid WarehouseId, int Page = 1, int PageSize = 50) : IRequest<WarehouseStockDto>;
public sealed record WarehouseDto(Guid Id, string Code, string Name, WarehouseKind Kind, string Street, string City, string PostalCode, string CountryCode, bool IsActive);
public sealed record WarehousePageDto(int Page, int PageSize, int Total, IReadOnlyList<WarehouseDto> Items);
public sealed record StockParcelDto(Guid ShipmentId, string TrackingNumber, DateTimeOffset LastScanAtUtc);
public sealed record WarehouseStockDto(Guid WarehouseId, int Page, int PageSize, int Total, IReadOnlyList<StockParcelDto> Items);
public sealed record ScanDto(Guid ScanId, Guid ShipmentId, Guid WarehouseId, string TrackingNumber, ScanDirection Direction, DateTimeOffset OccurredAtUtc);
public sealed class WarehouseNotFoundException(string message) : Exception(message);
public sealed class WarehouseConflictException(string message) : Exception(message);
public static class WarehouseMapping
{
    public static WarehouseDto ToDto(this WarehouseSite site) => new(site.Id, site.Code, site.Name, site.Kind, site.Street, site.City, site.PostalCode, site.CountryCode, site.IsActive);
    public static ScanDto ToDto(this WarehouseScan scan) => new(scan.ScanId, scan.ShipmentId, scan.WarehouseId, scan.TrackingNumber, scan.Direction, scan.OccurredAtUtc);
}
