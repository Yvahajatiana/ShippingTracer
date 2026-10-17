namespace ParcelFlow.BuildingBlocks.Contracts.Billing.V1;

public sealed record PaymentRequested(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid ShipmentId, string CustomerSubject, decimal Amount, string Currency)
    : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);

public sealed record PaymentCompleted(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid ShipmentId, decimal Amount, string Currency) : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);

public sealed record PaymentFailed(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid ShipmentId, string Reason) : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);
