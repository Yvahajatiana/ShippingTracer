using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.Delivery.Application;

namespace ParcelFlow.Delivery.Infrastructure.Consumers;

public sealed class ShipmentReadyForPickupConsumer(ISender sender) : IConsumer<ShipmentReadyForPickup>
{
    public async Task Consume(ConsumeContext<ShipmentReadyForPickup> context) =>
        await sender.Send(new RegisterReadyShipment(context.Message.ShipmentId, context.Message.TrackingNumber), context.CancellationToken);
}
public sealed class ParcelPickedUpConsumer(ISender sender) : IConsumer<ParcelPickedUp>
{
    public async Task Consume(ConsumeContext<ParcelPickedUp> context) =>
        await sender.Send(new RegisterParcelPickup(context.Message.ShipmentId), context.CancellationToken);
}
