namespace ParcelFlow.BuildingBlocks.Contracts;

/// <summary>Technical metadata shared by versioned integration event contracts.</summary>
/// <param name="EventId">Stable identifier, preserved when the same event is redelivered.</param>
/// <param name="CorrelationId">Identifier shared by messages in the same business flow.</param>
/// <param name="OccurredAtUtc">Time of the event, expressed with a UTC offset of zero.</param>
public abstract record IntegrationEvent(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc);
