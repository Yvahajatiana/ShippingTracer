using System.Text.Json.Serialization;

namespace ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;

[JsonConverter(typeof(JsonStringEnumConverter<TrackingStatus>))]
public enum TrackingStatus
{
    Created = 0,
    PickedUp = 1,
    InTransit = 2,
    AtSortingCenter = 3,
    OutForDelivery = 4,
    Delivered = 5,
    Failed = 6,
    Returned = 7
}

/// <summary>A logistics fact recorded by a warehouse or delivery service.</summary>
public sealed record ParcelStatusRecorded(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid ShipmentId, string TrackingNumber, TrackingStatus Status, string? FacilityCode)
    : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);

/// <summary>A tracking history entry made available under a registered tracking number.</summary>
public sealed record TrackingStatusChanged(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid ShipmentId, string TrackingNumber, TrackingStatus Status, string? FacilityCode, Guid SourceEventId)
    : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);
