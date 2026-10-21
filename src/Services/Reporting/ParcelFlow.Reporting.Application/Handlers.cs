using FluentValidation;
using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Contracts.Reporting.V1;
using ParcelFlow.Reporting.Domain;

namespace ParcelFlow.Reporting.Application;

public sealed class RecordStatusFactHandler(IReportingStore store, IPublishEndpoint publisher, TimeProvider clock)
    : IRequestHandler<RecordStatusFact, bool>
{
    public async Task<bool> Handle(RecordStatusFact request, CancellationToken ct)
    {
        await new RecordStatusFactValidator().ValidateAndThrowAsync(request, ct);
        await store.LockAsync(request.EventId, request.SourceEventId, request.ShipmentId, ct);
        var envelope = await store.FindFactByEventIdAsync(request.EventId, ct);
        if (envelope is not null && !Matches(envelope, request))
            throw new ReportingConflictException("The event identifier already identifies a different tracking fact.");
        var source = await store.FindFactBySourceIdAsync(request.SourceEventId, ct);
        if (source is not null && !Matches(source, request))
            throw new ReportingConflictException("The source identifier already identifies a different tracking fact.");
        var shipment = await store.FindShipmentAsync(request.ShipmentId, ct);
        if (shipment is not null && shipment.TrackingNumber != request.TrackingNumber)
            throw new ReportingConflictException("A shipment tracking number cannot change.");
        if (envelope is not null || source is not null) return false;
        if (shipment is null)
        {
            shipment = new ShipmentMetrics(request.ShipmentId, request.TrackingNumber);
            store.AddShipment(shipment);
        }
        shipment.AddFact(new StatusFact(request.EventId, request.SourceEventId, request.ShipmentId,
            request.Status, request.OccurredAtUtc, request.FacilityCode));
        await publisher.Publish(new ShipmentMetricsUpdated(Guid.NewGuid(), request.ShipmentId,
            ReportingRules.NormalizeTimestamp(clock.GetUtcNow()), request.ShipmentId, request.SourceEventId), ct);
        await store.SaveChangesAsync(ct);
        return true;
    }
    private static bool Matches(StatusFact fact, RecordStatusFact request) => fact.Matches(request.SourceEventId,
        request.ShipmentId, request.Status, request.OccurredAtUtc, request.FacilityCode);
}
public sealed class GetKpisHandler(IReportingStore store, TimeProvider clock) : IRequestHandler<GetKpis, KpiReportDto>
{
    public async Task<KpiReportDto> Handle(GetKpis request, CancellationToken ct)
    {
        await new GetKpisValidator().ValidateAndThrowAsync(request, ct);
        return await store.GetKpisAsync(request.From, request.To, ReportingRules.NormalizeTimestamp(clock.GetUtcNow()), ct);
    }
}
