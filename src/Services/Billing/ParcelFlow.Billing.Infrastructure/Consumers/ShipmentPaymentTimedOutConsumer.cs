using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.Billing.Application;

namespace ParcelFlow.Billing.Infrastructure.Consumers;

/// <summary>Cancels or refunds the invoice of a shipment whose booking saga closed on a payment timeout.</summary>
public sealed class ShipmentPaymentTimedOutConsumer(ISender sender) : IConsumer<ShipmentPaymentTimedOut>
{
    public async Task Consume(ConsumeContext<ShipmentPaymentTimedOut> context) =>
        await sender.Send(new SettleShipmentPaymentTimeout(context.Message.ShipmentId, context.Message.OccurredAtUtc), context.CancellationToken);
}
