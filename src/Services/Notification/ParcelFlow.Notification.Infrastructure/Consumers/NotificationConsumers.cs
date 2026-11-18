using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;
using ParcelFlow.Notification.Application;
using ParcelFlow.Notification.Domain;

namespace ParcelFlow.Notification.Infrastructure.Consumers;

/// <summary>Maps the consumed events to RecordNotification, shared by the consumers and the recipient prefetch.</summary>
internal static class NotificationCommands
{
    public static RecordNotification From(ShipmentCreated item) => new(item.EventId, item.ShipmentId, NotificationKind.ShipmentCreated, item.OccurredAtUtc,
        item.CustomerSubject, item.TrackingNumber, null, null, null, null, null);
    public static RecordNotification From(TrackingStatusChanged item)
    {
        var status = item.Status switch
        {
            TrackingStatus.Created => ParcelStatus.Created, TrackingStatus.PickedUp => ParcelStatus.PickedUp,
            TrackingStatus.InTransit => ParcelStatus.InTransit, TrackingStatus.AtSortingCenter => ParcelStatus.AtSortingCenter,
            TrackingStatus.OutForDelivery => ParcelStatus.OutForDelivery, TrackingStatus.Delivered => ParcelStatus.Delivered,
            TrackingStatus.Failed => ParcelStatus.Failed, TrackingStatus.Returned => ParcelStatus.Returned,
            _ => throw new ArgumentOutOfRangeException(nameof(item.Status), "Unknown parcel status.")
        };
        return new(item.EventId, item.ShipmentId, NotificationKind.TrackingStatus, item.OccurredAtUtc, null, item.TrackingNumber, status, null, null, null, null);
    }
    public static RecordNotification From(InvoiceIssued item) => new(item.EventId, item.ShipmentId, NotificationKind.InvoiceIssued, item.OccurredAtUtc,
        item.CustomerSubject, null, null, item.InvoiceId, null, item.Amount, item.Currency);
    public static RecordNotification From(PaymentCompleted item) => new(item.EventId, item.ShipmentId, NotificationKind.PaymentCompleted, item.OccurredAtUtc,
        null, null, null, null, null, item.Amount, item.Currency);
    public static RecordNotification From(PaymentFailed item) => new(item.EventId, item.ShipmentId, NotificationKind.PaymentFailed, item.OccurredAtUtc,
        null, null, null, null, null, null, null);
    public static RecordNotification From(CreditNoteIssued item) => new(item.EventId, item.ShipmentId, NotificationKind.CreditNoteIssued, item.OccurredAtUtc,
        item.CustomerSubject, null, null, item.InvoiceId, item.CreditNoteId, item.Amount, item.Currency);

    /// <summary>The command of a consumed event, or null when the event is not mapped or its status is unknown.</summary>
    public static RecordNotification? TryFrom(object message)
    {
        try
        {
            return message switch
            {
                ShipmentCreated item => From(item), TrackingStatusChanged item => From(item), InvoiceIssued item => From(item),
                PaymentCompleted item => From(item), PaymentFailed item => From(item), CreditNoteIssued item => From(item),
                _ => null
            };
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
public sealed class ShipmentCreatedConsumer(ISender sender) : IConsumer<ShipmentCreated>
{
    public Task Consume(ConsumeContext<ShipmentCreated> context) => sender.Send(NotificationCommands.From(context.Message), context.CancellationToken);
}
public sealed class TrackingStatusChangedConsumer(ISender sender) : IConsumer<TrackingStatusChanged>
{
    public Task Consume(ConsumeContext<TrackingStatusChanged> context) => sender.Send(NotificationCommands.From(context.Message), context.CancellationToken);
}
public sealed class InvoiceIssuedConsumer(ISender sender) : IConsumer<InvoiceIssued>
{
    public Task Consume(ConsumeContext<InvoiceIssued> context) => sender.Send(NotificationCommands.From(context.Message), context.CancellationToken);
}
public sealed class PaymentCompletedConsumer(ISender sender) : IConsumer<PaymentCompleted>
{
    public Task Consume(ConsumeContext<PaymentCompleted> context) => sender.Send(NotificationCommands.From(context.Message), context.CancellationToken);
}
public sealed class PaymentFailedConsumer(ISender sender) : IConsumer<PaymentFailed>
{
    public Task Consume(ConsumeContext<PaymentFailed> context) => sender.Send(NotificationCommands.From(context.Message), context.CancellationToken);
}
public sealed class CreditNoteIssuedConsumer(ISender sender) : IConsumer<CreditNoteIssued>
{
    public Task Consume(ConsumeContext<CreditNoteIssued> context) => sender.Send(NotificationCommands.From(context.Message), context.CancellationToken);
}
