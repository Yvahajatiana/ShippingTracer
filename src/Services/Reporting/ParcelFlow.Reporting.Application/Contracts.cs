using MediatR;
using ParcelFlow.Reporting.Domain;

namespace ParcelFlow.Reporting.Application;

public interface IReportingCommand { }
public sealed record RecordStatusFact(Guid EventId, Guid SourceEventId, Guid ShipmentId, string TrackingNumber,
    ParcelStatus Status, DateTimeOffset OccurredAtUtc, string? FacilityCode) : IRequest<bool>, IReportingCommand;
public sealed record GetKpis(DateOnly From, DateOnly To) : IRequest<KpiReportDto>;
public interface IReportingStore
{
    Task LockAsync(Guid eventId, Guid sourceEventId, Guid shipmentId, CancellationToken cancellationToken);
    Task<StatusFact?> FindFactByEventIdAsync(Guid eventId, CancellationToken cancellationToken);
    Task<StatusFact?> FindFactBySourceIdAsync(Guid sourceEventId, CancellationToken cancellationToken);
    Task<ShipmentMetrics?> FindShipmentAsync(Guid shipmentId, CancellationToken cancellationToken);
    void AddShipment(ShipmentMetrics shipment);
    void RememberEnvelope(Guid eventId, Guid sourceEventId);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<KpiReportDto> GetKpisAsync(DateOnly from, DateOnly to, DateTimeOffset generatedAtUtc, CancellationToken cancellationToken);
}
public sealed record KpiReportDto(DateOnly From, DateOnly To, DateTimeOffset GeneratedAtUtc,
    long TotalShipments, long DeliveredShipments, decimal? DeliveryRatePercent,
    decimal? AverageDeliveryHours, long DeliveryDurationSamples, decimal? AverageTransitHours,
    long TransitDurationSamples, IReadOnlyList<DailyVolumeDto> DailyVolumes, IReadOnlyList<StatusVolumeDto> StatusVolumes);
public sealed record DailyVolumeDto(DateOnly Date, long Shipments);
public sealed record StatusVolumeDto(ParcelStatus Status, long Shipments);
public sealed class ReportingConflictException(string message) : Exception(message);
