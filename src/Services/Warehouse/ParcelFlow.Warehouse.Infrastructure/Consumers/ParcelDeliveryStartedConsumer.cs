using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Warehouse.Application;

namespace ParcelFlow.Warehouse.Infrastructure.Consumers;

/// <summary>
/// Releases Warehouse stock when Delivery reports that a parcel left for delivery (OutForDelivery) or,
/// as a safety net when that event was missed, that a delivery attempt completed (Delivered or Failed).
/// Other tracking statuses, including those published by Warehouse itself, are ignored.
/// </summary>
public sealed class ParcelDeliveryStartedConsumer(ISender sender, ILogger<ParcelDeliveryStartedConsumer> logger) : IConsumer<ParcelStatusRecorded>
{
    public async Task Consume(ConsumeContext<ParcelStatusRecorded> context)
    {
        var message = context.Message;
        // A Failed stop also proves the parcel left the depot; a later return-to-depot Entry scan is newer than
        // the event, so the handler's superseded guard keeps it in stock even if the event is redelivered.
        if (message.Status is not (TrackingStatus.OutForDelivery or TrackingStatus.Delivered or TrackingStatus.Failed)) return;
        var outcome = await sender.Send(new RecordDeliveryExit(message.EventId, message.ShipmentId, message.TrackingNumber, message.OccurredAtUtc), context.CancellationToken);
        if (outcome == DeliveryExitOutcome.Recorded)
            logger.LogInformation("Automatic warehouse exit recorded for shipment {ShipmentId} on delivery status {Status} (event {EventId}).", message.ShipmentId, message.Status, message.EventId);
        else
            logger.LogDebug("No automatic warehouse exit for shipment {ShipmentId} on delivery status {Status} (event {EventId}): {Outcome}.", message.ShipmentId, message.Status, message.EventId, outcome);
    }
}
