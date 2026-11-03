namespace ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;

public sealed record ShipmentCreated(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid ShipmentId, string TrackingNumber, string CustomerSubject, decimal Amount, string Currency)
    : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);

public sealed record ShipmentReadyForPickup(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid ShipmentId, string TrackingNumber) : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);

public sealed record ShipmentPaymentTimedOut(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid ShipmentId, string TrackingNumber) : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);

public sealed record ParcelPickedUp(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid ShipmentId) : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);
