using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Warehouse.Application;

namespace ParcelFlow.Warehouse.Infrastructure.Consumers;

/// <summary>
/// Releases Warehouse stock when Delivery reports that a parcel left for delivery (OutForDelivery) or,
/// as a safety net, was delivered. Other tracking statuses, including those published by Warehouse itself, are ignored.
/// </summary>
public sealed class ParcelDeliveryStartedConsumer(ISender sender, ILogger<ParcelDeliveryStartedConsumer> logger) : IConsumer<ParcelStatusRecorded>
{
    public async Task Consume(ConsumeContext<ParcelStatusRecorded> context)
    {
        var message = context.Message;
        if (message.Status is not (TrackingStatus.OutForDelivery or TrackingStatus.Delivered)) return;
        var outcome = await sender.Send(new RecordDeliveryExit(message.EventId, message.ShipmentId, message.TrackingNumber, message.OccurredAtUtc), context.CancellationToken);
        if (outcome == DeliveryExitOutcome.Recorded)
            logger.LogInformation("Automatic warehouse exit recorded for shipment {ShipmentId} on delivery status {Status} (event {EventId}).", message.ShipmentId, message.Status, message.EventId);
        else
            logger.LogDebug("No automatic warehouse exit for shipment {ShipmentId} on delivery status {Status} (event {EventId}): {Outcome}.", message.ShipmentId, message.Status, message.EventId, outcome);
    }
}
