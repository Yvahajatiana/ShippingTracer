using System.Globalization;
using FluentValidation;
using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Contracts.Notification.V1;
using ParcelFlow.Notification.Domain;

namespace ParcelFlow.Notification.Application;

public sealed class RecordNotificationHandler(INotificationStore store, IShipmentRecipientResolver recipients,
    IPublishEndpoint publisher, TimeProvider clock) : IRequestHandler<RecordNotification, bool>
{
    public async Task<bool> Handle(RecordNotification request, CancellationToken ct)
    {
        await new RecordNotificationValidator().ValidateAndThrowAsync(request, ct);
        if (request.Kind == NotificationKind.TrackingStatus && request.Status == ParcelStatus.Created) return false;
        await store.LockEventAsync(request.EventId, ct);
        var existing = await store.FindEventAsync(request.EventId, ct);
        if (existing is not null)
        {
            if (!existing.Matches(request.ShipmentId, request.Kind, request.OccurredAtUtc, request.CustomerSubject, request.TrackingNumber,
                request.Status, request.InvoiceId, request.CreditNoteId, request.Amount, request.Currency))
                throw new NotificationConflictException("The source event identifier already has different immutable details.");
            return false;
        }
        var recipient = await recipients.ResolveAsync(request.ShipmentId, ct);
        if (request.CustomerSubject is not null && request.CustomerSubject != recipient.CustomerSubject ||
            request.TrackingNumber is not null && request.TrackingNumber != recipient.TrackingNumber)
            throw new NotificationConflictException("The event recipient conflicts with the original shipment.");
        var notification = new NotificationEvent(request.EventId, request.ShipmentId, request.Kind, request.OccurredAtUtc,
            request.CustomerSubject, request.TrackingNumber, request.Status, request.InvoiceId, request.CreditNoteId, request.Amount, request.Currency);
        var (subject, description) = Content(request);
        var observed = notification.OccurredAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        var body = $"Colis {recipient.TrackingNumber} : {description} Événement observé à {observed}.";
        notification.RecordMessages(recipient.CustomerSubject, recipient.TrackingNumber, recipient.Email, recipient.Phone, subject, body, body, clock.GetUtcNow());
        store.AddEvent(notification);
        foreach (var message in notification.Messages)
            await publisher.Publish(new NotificationRecorded(Guid.NewGuid(), message.ShipmentId, message.RecordedAtUtc,
                message.Id, message.SourceEventId, message.ShipmentId, message.CustomerSubject, message.Channel.ToString()), ct);
        await store.SaveChangesAsync(ct);
        return true;
    }
    private static (string Subject, string Description) Content(RecordNotification request)
    {
        var amount = request.Amount?.ToString("0.00", CultureInfo.InvariantCulture);
        return request.Kind switch
        {
            NotificationKind.ShipmentCreated => ("Création de votre envoi", "votre envoi a été créé."),
            NotificationKind.TrackingStatus => ("Événement de suivi de votre colis", $"statut observé : {StatusLabel(request.Status!.Value)}."),
            NotificationKind.InvoiceIssued => ("Facture de votre envoi", $"facture INV-{request.InvoiceId!.Value.ToString("N").ToUpperInvariant()} de {amount} {request.Currency} émise."),
            NotificationKind.PaymentCompleted => ("Paiement de votre envoi", $"paiement simulé de {amount} {request.Currency} accepté."),
            NotificationKind.PaymentFailed => ("Paiement de votre envoi refusé", "paiement simulé refusé."),
            NotificationKind.CreditNoteIssued => ("Avoir sur votre envoi", $"avoir CRN-{request.CreditNoteId!.Value.ToString("N").ToUpperInvariant()} de {amount} {request.Currency} émis."),
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
    }
    private static string StatusLabel(ParcelStatus status) => status switch
    {
        ParcelStatus.Created => "Créé", ParcelStatus.PickedUp => "Pris en charge", ParcelStatus.InTransit => "En transit",
        ParcelStatus.AtSortingCenter => "En centre de tri", ParcelStatus.OutForDelivery => "En livraison", ParcelStatus.Delivered => "Livré",
        ParcelStatus.Failed => "Échec", ParcelStatus.Returned => "Retour", _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
public sealed class GetNotificationHandler(INotificationStore store, INotificationActor actor) : IRequestHandler<GetNotification, NotificationDto>
{
    public async Task<NotificationDto> Handle(GetNotification request, CancellationToken ct)
    {
        var notification = await store.FindNotificationAsync(request.NotificationId, ct) ?? throw new NotificationNotFoundException("Notification not found.");
        if (!NotificationRules.IsSubject(actor.Subject) || !actor.IsAdmin && notification.CustomerSubject != actor.Subject)
            throw new NotificationForbiddenException("Only the shipment owner or an administrator may access this notification.");
        return notification.ToDto();
    }
}
public sealed class ListNotificationsHandler(INotificationStore store, INotificationActor actor) : IRequestHandler<ListNotifications, NotificationPageDto>
{
    public Task<NotificationPageDto> Handle(ListNotifications request, CancellationToken ct)
    {
        if (!NotificationRules.IsSubject(actor.Subject)) throw new NotificationForbiddenException("A valid actor is required.");
        return store.ListNotificationsAsync(actor.IsAdmin ? null : actor.Subject, request.ShipmentId, request.Page, request.PageSize, ct);
    }
}
