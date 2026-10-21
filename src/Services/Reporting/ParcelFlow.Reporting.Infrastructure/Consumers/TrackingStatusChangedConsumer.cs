using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Reporting.Application;
using ParcelFlow.Reporting.Domain;
namespace ParcelFlow.Reporting.Infrastructure.Consumers;
public sealed class TrackingStatusChangedConsumer(ISender sender) : IConsumer<TrackingStatusChanged>
{
    public async Task Consume(ConsumeContext<TrackingStatusChanged> context)
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
        await sender.Send(new RecordStatusFact(message.EventId, message.SourceEventId, message.ShipmentId,
            message.TrackingNumber, status, message.OccurredAtUtc, message.FacilityCode), context.CancellationToken);
    }
}
