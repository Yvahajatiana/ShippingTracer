using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Application.Identity;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Warehouse.Domain;

namespace ParcelFlow.Warehouse.Application;

public sealed class CreateWarehouseHandler(IWarehouseStore store) : IRequestHandler<CreateWarehouse, WarehouseDto>
{
    public async Task<WarehouseDto> Handle(CreateWarehouse request, CancellationToken cancellationToken)
    {
        var warehouse = new WarehouseSite(request.Id, request.Code, request.Name, request.Kind, request.Street, request.City, request.PostalCode, request.CountryCode);
        store.AddWarehouse(warehouse);
        await store.SaveChangesAsync(cancellationToken);
        return warehouse.ToDto();
    }
}
public sealed class UpdateWarehouseHandler(IWarehouseStore store) : IRequestHandler<UpdateWarehouse, WarehouseDto>
{
    public async Task<WarehouseDto> Handle(UpdateWarehouse request, CancellationToken cancellationToken)
    {
        var warehouse = await store.FindWarehouseAsync(request.Id, cancellationToken)
            ?? throw new WarehouseNotFoundException("Warehouse not found.");
        if (!request.IsActive && await store.HasStockAsync(request.Id, cancellationToken))
            throw new WarehouseConflictException("A warehouse containing parcels cannot be deactivated.");
        warehouse.Update(request.Name, request.Street, request.City, request.PostalCode, request.CountryCode, request.IsActive);
        await store.SaveChangesAsync(cancellationToken);
        return warehouse.ToDto();
    }
}
public sealed class RegisterReadyShipmentHandler(IWarehouseStore store) : IRequestHandler<RegisterReadyShipment, bool>
{
    public async Task<bool> Handle(RegisterReadyShipment request, CancellationToken cancellationToken)
    {
        var number = WarehouseRules.NormalizeTrackingNumber(request.TrackingNumber);
        await store.LockParcelAsync(number, cancellationToken);
        var existing = await store.FindInventoryByShipmentAsync(request.ShipmentId, cancellationToken);
        if (existing is not null)
        {
            if (existing.TrackingNumber != number) throw new WarehouseConflictException("Shipment already has a different tracking number.");
            return false;
        }
        if (await store.FindInventoryAsync(number, cancellationToken) is not null)
            throw new WarehouseConflictException("Tracking number already belongs to another shipment.");
        store.AddInventory(new(request.ShipmentId, number));
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }
}
public sealed class ScanParcelHandler(IWarehouseStore store, IPublishEndpoint publisher, TimeProvider clock) : IRequestHandler<ScanParcel, ScanDto>
{
    public async Task<ScanDto> Handle(ScanParcel request, CancellationToken cancellationToken)
    {
        var number = WarehouseRules.NormalizeTrackingNumber(request.TrackingNumber);
        await store.LockParcelAsync(number, cancellationToken);
        var existing = await store.FindScanAsync(request.ScanId, cancellationToken);
        if (existing is not null)
        {
            if (existing.WarehouseId != request.WarehouseId || existing.TrackingNumber != number || existing.Direction != request.Direction || existing.OperatorSubject != request.OperatorSubject.Trim())
                throw new WarehouseConflictException("Scan ID already identifies a different operation.");
            return existing.ToDto();
        }
        var inventory = await store.FindInventoryAsync(number, cancellationToken)
            ?? throw new WarehouseConflictException("Shipment is not ready for pickup.");
        var warehouse = await store.FindWarehouseAsync(request.WarehouseId, cancellationToken)
            ?? throw new WarehouseNotFoundException("Warehouse not found.");
        var now = WarehouseRules.NormalizeTimestamp(clock.GetUtcNow());
        if (inventory.LastScanAtUtc is { } previous && now <= previous) now = previous.AddTicks(10);
        bool firstPickup;
        try { firstPickup = inventory.ApplyScan(warehouse, request.Direction, now); }
        catch (InvalidOperationException exception) { throw new WarehouseConflictException(exception.Message); }
        var scan = new WarehouseScan(request.ScanId, inventory.ShipmentId, warehouse.Id, number, request.Direction, now, request.OperatorSubject);
        store.AddScan(scan);
        var isSortingEntry = request.Direction == ScanDirection.Entry && warehouse.Kind == WarehouseKind.SortingCenter;
        if (firstPickup)
            await publisher.Publish(new ParcelPickedUp(isSortingEntry ? DeterministicGuid.Create("warehouse:parcel-picked-up:", scan.ScanId) : scan.ScanId, inventory.ShipmentId, now, inventory.ShipmentId), cancellationToken);
        if (isSortingEntry || !firstPickup)
            await publisher.Publish(new ParcelStatusRecorded(scan.ScanId, inventory.ShipmentId, now, inventory.ShipmentId, number,
                isSortingEntry ? TrackingStatus.AtSortingCenter : TrackingStatus.InTransit, warehouse.Code), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return scan.ToDto();
    }
}
public sealed class RecordDeliveryExitHandler(IWarehouseStore store) : IRequestHandler<RecordDeliveryExit, DeliveryExitOutcome>
{
    public async Task<DeliveryExitOutcome> Handle(RecordDeliveryExit request, CancellationToken cancellationToken)
    {
        var number = WarehouseRules.NormalizeTrackingNumber(request.TrackingNumber);
        await store.LockParcelAsync(number, cancellationToken);
        // The scan ID is derived from the source event so a redelivered event can never move the parcel twice.
        var scanId = WarehouseRules.DeliveryExitScanId(request.SourceEventId);
        if (await store.FindScanAsync(scanId, cancellationToken) is not null) return DeliveryExitOutcome.AlreadyRecorded;
        var inventory = await store.FindInventoryAsync(number, cancellationToken);
        if (inventory is null || inventory.ShipmentId != request.ShipmentId) return DeliveryExitOutcome.UnknownParcel;
        if (inventory.CurrentWarehouseId is not { } warehouseId) return DeliveryExitOutcome.NotInStock;
        var at = WarehouseRules.NormalizeTimestamp(request.OccurredAtUtc);
        // A movement scanned after the delivery started (e.g. a return to the depot) wins over the late event.
        if (inventory.LastScanAtUtc is { } previous && at <= previous) return DeliveryExitOutcome.Superseded;
        var warehouse = await store.FindWarehouseAsync(warehouseId, cancellationToken)
            ?? throw new InvalidOperationException("Current warehouse of the parcel was not found.");
        inventory.ApplyScan(warehouse, ScanDirection.Exit, at);
        store.AddScan(new WarehouseScan(scanId, inventory.ShipmentId, warehouse.Id, number, ScanDirection.Exit, at, WarehouseRules.DeliveryStartOperator));
        // No tracking fact is published: Delivery already reported OutForDelivery or Delivered, and a
        // late InTransit status would regress the public tracking timeline.
        await store.SaveChangesAsync(cancellationToken);
        return DeliveryExitOutcome.Recorded;
    }
}
public sealed class GetWarehouseHandler(IWarehouseStore store) : IRequestHandler<GetWarehouse, WarehouseDto>
{
    public async Task<WarehouseDto> Handle(GetWarehouse request, CancellationToken cancellationToken) =>
        (await store.FindWarehouseAsync(request.Id, cancellationToken) ?? throw new WarehouseNotFoundException("Warehouse not found.")).ToDto();
}
public sealed class ListWarehousesHandler(IWarehouseStore store) : IRequestHandler<ListWarehouses, WarehousePageDto>
{
    public Task<WarehousePageDto> Handle(ListWarehouses request, CancellationToken cancellationToken) => store.ListWarehousesAsync(request.Page, request.PageSize, cancellationToken);
}
public sealed class GetWarehouseStockHandler(IWarehouseStore store) : IRequestHandler<GetWarehouseStock, WarehouseStockDto>
{
    public async Task<WarehouseStockDto> Handle(GetWarehouseStock request, CancellationToken cancellationToken)
    {
        _ = await store.FindWarehouseAsync(request.WarehouseId, cancellationToken) ?? throw new WarehouseNotFoundException("Warehouse not found.");
        return await store.GetStockAsync(request.WarehouseId, request.Page, request.PageSize, cancellationToken);
    }
}
