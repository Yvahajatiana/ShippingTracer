using MediatR;
using ParcelFlow.Notification.Domain;

namespace ParcelFlow.Notification.Application;

public interface INotificationActor { string Subject { get; } bool IsAdmin { get; } }
public interface INotificationCommand { }
public sealed record ShipmentRecipient(string CustomerSubject, string TrackingNumber, string Email, string Phone);
public interface IShipmentRecipientResolver
{
    Task<ShipmentRecipient> ResolveAsync(Guid shipmentId, CancellationToken cancellationToken);
}
public interface INotificationStore
{
    Task LockEventAsync(Guid eventId, CancellationToken cancellationToken);
    Task<NotificationEvent?> FindEventAsync(Guid eventId, CancellationToken cancellationToken);
    void AddEvent(NotificationEvent notificationEvent);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<NotificationMessage?> FindNotificationAsync(Guid notificationId, CancellationToken cancellationToken);
    Task<NotificationPageDto> ListNotificationsAsync(string? ownerSubject, Guid? shipmentId, int page, int pageSize, CancellationToken cancellationToken);
}
public sealed record RecordNotification(Guid EventId, Guid ShipmentId, NotificationKind Kind, DateTimeOffset OccurredAtUtc,
    string? CustomerSubject, string? TrackingNumber, ParcelStatus? Status, Guid? InvoiceId, Guid? CreditNoteId,
    decimal? Amount, string? Currency) : IRequest<bool>, INotificationCommand;
public sealed record GetNotification(Guid NotificationId) : IRequest<NotificationDto>;
public sealed record ListNotifications(Guid? ShipmentId = null, int Page = 1, int PageSize = 50) : IRequest<NotificationPageDto>;
public sealed record NotificationDto(Guid Id, Guid SourceEventId, Guid ShipmentId, string CustomerSubject, string TrackingNumber,
    NotificationChannel Channel, string Destination, string Subject, string Body, DateTimeOffset OccurredAtUtc,
    DateTimeOffset RecordedAtUtc, string DeliveryStatus);
public sealed record NotificationPageDto(int Page, int PageSize, int Total, IReadOnlyList<NotificationDto> Items);
public sealed class NotificationNotFoundException(string message) : Exception(message);
public sealed class NotificationConflictException(string message) : Exception(message);
public sealed class NotificationForbiddenException(string message) : Exception(message);
public static class NotificationMapping
{
    public static NotificationDto ToDto(this NotificationMessage message) => new(message.Id, message.SourceEventId, message.ShipmentId,
        message.CustomerSubject, message.TrackingNumber, message.Channel, message.Destination, message.Subject, message.Body,
        message.OccurredAtUtc, message.RecordedAtUtc, message.DeliveryStatus);
}
