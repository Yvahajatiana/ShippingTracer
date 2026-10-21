namespace ParcelFlow.BuildingBlocks.Contracts.Reporting.V1;

public sealed record ShipmentMetricsUpdated(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid ShipmentId, Guid SourceEventId) : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);
