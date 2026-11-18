using MassTransit;
using Microsoft.EntityFrameworkCore;
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Notification.Application;
using ParcelFlow.Notification.Infrastructure.Persistence;

namespace ParcelFlow.Notification.Infrastructure.Consumers;

/// <summary>
/// Resolves the recipient (Keycloak token, then Shipment API) before the Entity Framework outbox
/// opens the consumer transaction and locks the inbox row, so a slow Shipment never holds a
/// PostgreSQL connection and its locks. Failures go through the endpoint retry like before.
/// Already recorded events skip the lookup; a concurrent duplicate may still resolve once more.
/// </summary>
public sealed class RecipientPrefetchFilter<T>(IShipmentRecipientResolver resolver, PrefetchedRecipients prefetched,
    NotificationDbContext database) : IFilter<ConsumeContext<T>> where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        if (Target(context.Message) is var (eventId, shipmentId) &&
            !await database.Events.AsNoTracking().AnyAsync(x => x.EventId == eventId, context.CancellationToken))
            prefetched.Add(shipmentId, await resolver.ResolveAsync(shipmentId, context.CancellationToken));
        await next.Send(context);
    }

    // Same events as the consumers. Tracking "Created" is never notified and an unknown status is
    // rejected by its consumer: neither needs a recipient.
    private static (Guid EventId, Guid ShipmentId)? Target(T message) => message switch
    {
        ShipmentCreated item => (item.EventId, item.ShipmentId),
        TrackingStatusChanged item when item.Status != TrackingStatus.Created && Enum.IsDefined(item.Status) => (item.EventId, item.ShipmentId),
        InvoiceIssued item => (item.EventId, item.ShipmentId),
        PaymentCompleted item => (item.EventId, item.ShipmentId),
        PaymentFailed item => (item.EventId, item.ShipmentId),
        CreditNoteIssued item => (item.EventId, item.ShipmentId),
        _ => null
    };

    public void Probe(ProbeContext context) => context.CreateFilterScope("notification-recipient-prefetch");
}
