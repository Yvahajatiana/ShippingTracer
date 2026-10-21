namespace ParcelFlow.BuildingBlocks.Contracts.Billing.V1;

public sealed record InvoiceIssued(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid InvoiceId, Guid ShipmentId, string CustomerSubject, decimal Amount, string Currency)
    : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);

public sealed record CreditNoteIssued(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid CreditNoteId, Guid InvoiceId, Guid ShipmentId, string CustomerSubject, decimal Amount, string Currency)
    : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);
