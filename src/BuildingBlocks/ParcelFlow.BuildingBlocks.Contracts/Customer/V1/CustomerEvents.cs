namespace ParcelFlow.BuildingBlocks.Contracts.Customer.V1;

public sealed record CustomerProfileCreated(
    Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid CustomerId, string KeycloakSubject)
    : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);

public sealed record CustomerProfileUpdated(
    Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc, Guid CustomerId)
    : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);

public sealed record CompanyCreated(
    Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc, Guid CompanyId)
    : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);

public sealed record CompanyUpdated(
    Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc, Guid CompanyId)
    : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);
