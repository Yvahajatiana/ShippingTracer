using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.Warehouse.Application;

namespace ParcelFlow.Warehouse.Infrastructure.Consumers;

public sealed class ShipmentReadyForPickupConsumer(ISender sender) : IConsumer<ShipmentReadyForPickup>
{
    public async Task Consume(ConsumeContext<ShipmentReadyForPickup> context) =>
        await sender.Send(new RegisterReadyShipment(context.Message.ShipmentId, context.Message.TrackingNumber), context.CancellationToken);
}
