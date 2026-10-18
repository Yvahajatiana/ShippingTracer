namespace ParcelFlow.BuildingBlocks.Contracts.Notification.V1;

public sealed record NotificationRecorded(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid NotificationId, Guid SourceEventId, Guid ShipmentId, string CustomerSubject, string Channel)
    : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);
