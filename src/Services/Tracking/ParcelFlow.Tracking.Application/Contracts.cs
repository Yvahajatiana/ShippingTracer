using MediatR;
using ParcelFlow.Tracking.Domain;

namespace ParcelFlow.Tracking.Application;

public interface ITrackingStore
{
    Task<TrackingRecord?> FindByShipmentAsync(Guid shipmentId, CancellationToken cancellationToken);
    Task<TrackingRecord?> FindByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken);
    void Add(TrackingRecord record);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class TrackingNotFoundException(string message) : Exception(message);
public sealed class TrackingConflictException(string message) : Exception(message);
public sealed record RecordTrackingEvent(Guid EventId, Guid ShipmentId, string? TrackingNumber, ParcelStatus Status,
    DateTimeOffset OccurredAtUtc, string? FacilityCode) : IRequest<bool>;
public sealed record GetPublicTracking(string TrackingNumber) : IRequest<PublicTrackingDto>;
public sealed record GetShipmentTracking(Guid ShipmentId) : IRequest<InternalTrackingDto>;
public sealed record PublicTrackingDto(string TrackingNumber, ParcelStatus CurrentStatus, string CurrentStatusLabel,
    DateTimeOffset UpdatedAtUtc, IReadOnlyList<PublicTrackingEntryDto> History);
public sealed record PublicTrackingEntryDto(ParcelStatus Status, string StatusLabel, DateTimeOffset OccurredAtUtc, string? FacilityCode);
public sealed record InternalTrackingDto(Guid ShipmentId, string? TrackingNumber, ParcelStatus CurrentStatus,
    string CurrentStatusLabel, DateTimeOffset UpdatedAtUtc, IReadOnlyList<InternalTrackingEntryDto> History);
public sealed record InternalTrackingEntryDto(Guid EventId, ParcelStatus Status, string StatusLabel,
    DateTimeOffset OccurredAtUtc, string? FacilityCode);

public static class TrackingMapping
{
    public static PublicTrackingDto ToPublicDto(this TrackingRecord record) => new(
        record.TrackingNumber ?? throw new InvalidOperationException("Tracking number is not registered."), record.CurrentStatus,
        record.CurrentStatus.Label(), record.UpdatedAtUtc, record.Timeline.Select(x => new PublicTrackingEntryDto(
            x.Status, x.Status.Label(), x.OccurredAtUtc, x.FacilityCode)).ToArray());
    public static InternalTrackingDto ToInternalDto(this TrackingRecord record) => new(record.ShipmentId, record.TrackingNumber,
        record.CurrentStatus, record.CurrentStatus.Label(), record.UpdatedAtUtc,
        record.Timeline.Select(x => new InternalTrackingEntryDto(x.EventId, x.Status, x.Status.Label(), x.OccurredAtUtc, x.FacilityCode)).ToArray());
}
