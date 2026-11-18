using MassTransit;
using Microsoft.EntityFrameworkCore;
using ParcelFlow.Notification.Application;
using ParcelFlow.Notification.Domain;
using ParcelFlow.Notification.Infrastructure.Persistence;

namespace ParcelFlow.Notification.Infrastructure.Consumers;

/// <summary>
/// Resolves the recipient (Keycloak token, then Shipment API) before the Entity Framework outbox
/// opens the consumer transaction and locks the inbox row, so a slow Shipment never holds a
/// PostgreSQL connection and its locks. Failures go through the endpoint retry like before.
/// Events the handler would reject or ignore skip the lookup (unknown status, invalid metadata,
/// tracking "Created", already recorded); a concurrent duplicate may still resolve once more.
/// </summary>
public sealed class RecipientPrefetchFilter<T>(IShipmentRecipientResolver resolver, PrefetchedRecipients prefetched,
    NotificationDbContext database) : IFilter<ConsumeContext<T>> where T : class
{
    private static readonly RecordNotificationValidator Validator = new();

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        if (NotificationCommands.TryFrom(context.Message) is { } command &&
            !(command.Kind == NotificationKind.TrackingStatus && command.Status == ParcelStatus.Created) &&
            (await Validator.ValidateAsync(command, context.CancellationToken)).IsValid &&
            !await database.Events.AsNoTracking().AnyAsync(x => x.EventId == command.EventId, context.CancellationToken))
            prefetched.Add(command.ShipmentId, await resolver.ResolveAsync(command.ShipmentId, context.CancellationToken));
        await next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("notification-recipient-prefetch");
}
