using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;
using ParcelFlow.Notification.Application;
using ParcelFlow.Notification.Domain;

namespace ParcelFlow.Notification.Infrastructure.Consumers;

public sealed class ShipmentCreatedConsumer(ISender sender) : IConsumer<ShipmentCreated>
{
    public async Task Consume(ConsumeContext<ShipmentCreated> context)
    {
        var item = context.Message;
        await sender.Send(new RecordNotification(item.EventId, item.ShipmentId, NotificationKind.ShipmentCreated, item.OccurredAtUtc,
            item.CustomerSubject, item.TrackingNumber, null, null, null, null, null), context.CancellationToken);
    }
}
public sealed class TrackingStatusChangedConsumer(ISender sender) : IConsumer<TrackingStatusChanged>
{
    public async Task Consume(ConsumeContext<TrackingStatusChanged> context)
    {
        var item = context.Message;
        var status = item.Status switch
        {
            TrackingStatus.Created => ParcelStatus.Created, TrackingStatus.PickedUp => ParcelStatus.PickedUp,
            TrackingStatus.InTransit => ParcelStatus.InTransit, TrackingStatus.AtSortingCenter => ParcelStatus.AtSortingCenter,
            TrackingStatus.OutForDelivery => ParcelStatus.OutForDelivery, TrackingStatus.Delivered => ParcelStatus.Delivered,
            TrackingStatus.Failed => ParcelStatus.Failed, TrackingStatus.Returned => ParcelStatus.Returned,
            _ => throw new ArgumentOutOfRangeException(nameof(item.Status), "Unknown parcel status.")
        };
        await sender.Send(new RecordNotification(item.EventId, item.ShipmentId, NotificationKind.TrackingStatus, item.OccurredAtUtc,
            null, item.TrackingNumber, status, null, null, null, null), context.CancellationToken);
    }
}
public sealed class InvoiceIssuedConsumer(ISender sender) : IConsumer<InvoiceIssued>
{
    public async Task Consume(ConsumeContext<InvoiceIssued> context)
    {
        var item = context.Message;
        await sender.Send(new RecordNotification(item.EventId, item.ShipmentId, NotificationKind.InvoiceIssued, item.OccurredAtUtc,
            item.CustomerSubject, null, null, item.InvoiceId, null, item.Amount, item.Currency), context.CancellationToken);
    }
}
public sealed class PaymentCompletedConsumer(ISender sender) : IConsumer<PaymentCompleted>
{
    public async Task Consume(ConsumeContext<PaymentCompleted> context)
    {
        var item = context.Message;
        await sender.Send(new RecordNotification(item.EventId, item.ShipmentId, NotificationKind.PaymentCompleted, item.OccurredAtUtc,
            null, null, null, null, null, item.Amount, item.Currency), context.CancellationToken);
    }
}
public sealed class PaymentFailedConsumer(ISender sender) : IConsumer<PaymentFailed>
{
    public async Task Consume(ConsumeContext<PaymentFailed> context)
    {
        var item = context.Message;
        await sender.Send(new RecordNotification(item.EventId, item.ShipmentId, NotificationKind.PaymentFailed, item.OccurredAtUtc,
            null, null, null, null, null, null, null), context.CancellationToken);
    }
}
public sealed class CreditNoteIssuedConsumer(ISender sender) : IConsumer<CreditNoteIssued>
{
    public async Task Consume(ConsumeContext<CreditNoteIssued> context)
    {
        var item = context.Message;
        await sender.Send(new RecordNotification(item.EventId, item.ShipmentId, NotificationKind.CreditNoteIssued, item.OccurredAtUtc,
            item.CustomerSubject, null, null, item.InvoiceId, item.CreditNoteId, item.Amount, item.Currency), context.CancellationToken);
    }
}
