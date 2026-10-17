using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Tracking.Application;
using ParcelFlow.Tracking.Domain;

namespace ParcelFlow.Tracking.Infrastructure.Consumers;

public sealed class ShipmentCreatedConsumer(ISender sender) : IConsumer<ShipmentCreated>
{
    public async Task Consume(ConsumeContext<ShipmentCreated> context)
    {
        var message = context.Message;
        await sender.Send(new RecordTrackingEvent(message.EventId, message.ShipmentId, message.TrackingNumber,
            ParcelStatus.Created, message.OccurredAtUtc, null), context.CancellationToken);
    }
}

public sealed class ParcelPickedUpConsumer(ISender sender) : IConsumer<ParcelPickedUp>
{
    public async Task Consume(ConsumeContext<ParcelPickedUp> context)
    {
        var message = context.Message;
        await sender.Send(new RecordTrackingEvent(message.EventId, message.ShipmentId, null,
            ParcelStatus.PickedUp, message.OccurredAtUtc, null), context.CancellationToken);
    }
}

public sealed class ParcelStatusRecordedConsumer(ISender sender) : IConsumer<ParcelStatusRecorded>
{
    public async Task Consume(ConsumeContext<ParcelStatusRecorded> context)
    {
        var message = context.Message;
        var status = message.Status switch
        {
            TrackingStatus.Created => ParcelStatus.Created,
            TrackingStatus.PickedUp => ParcelStatus.PickedUp,
            TrackingStatus.InTransit => ParcelStatus.InTransit,
            TrackingStatus.AtSortingCenter => ParcelStatus.AtSortingCenter,
            TrackingStatus.OutForDelivery => ParcelStatus.OutForDelivery,
            TrackingStatus.Delivered => ParcelStatus.Delivered,
            TrackingStatus.Failed => ParcelStatus.Failed,
            TrackingStatus.Returned => ParcelStatus.Returned,
            _ => throw new ArgumentOutOfRangeException(nameof(context), "Unknown tracking status.")
        };
        await sender.Send(new RecordTrackingEvent(message.EventId, message.ShipmentId, message.TrackingNumber,
            status, message.OccurredAtUtc, message.FacilityCode), context.CancellationToken);
    }
}
