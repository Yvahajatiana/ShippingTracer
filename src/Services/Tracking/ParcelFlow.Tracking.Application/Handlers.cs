using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Tracking.Domain;

namespace ParcelFlow.Tracking.Application;

public sealed class RecordTrackingEventHandler(ITrackingStore store, IPublishEndpoint publisher) : IRequestHandler<RecordTrackingEvent, bool>
{
    public async Task<bool> Handle(RecordTrackingEvent request, CancellationToken cancellationToken)
    {
        var record = await store.FindByShipmentAsync(request.ShipmentId, cancellationToken);
        var isNew = record is null;
        record ??= new TrackingRecord(request.ShipmentId);
        var wasRegistered = record.TrackingNumber is not null;
        bool added;
        try
        {
            if (request.TrackingNumber is not null) record.Register(request.TrackingNumber);
            added = record.AddEntry(new TrackingEntry(request.EventId, request.Status, request.OccurredAtUtc, request.FacilityCode));
        }
        catch (InvalidOperationException exception) { throw new TrackingConflictException(exception.Message); }
        if (isNew) store.Add(record);
        if (record.TrackingNumber is not null)
        {
            var publish = !wasRegistered ? record.Timeline : added ? record.Entries.Where(x => x.EventId == request.EventId) : [];
            foreach (var entry in publish)
                await publisher.Publish(new TrackingStatusChanged(Guid.NewGuid(), record.ShipmentId, entry.OccurredAtUtc,
                    record.ShipmentId, record.TrackingNumber, Map(entry.Status), entry.FacilityCode, entry.EventId), cancellationToken);
        }
        if (added || (!wasRegistered && record.TrackingNumber is not null)) await store.SaveChangesAsync(cancellationToken);
        return added;
    }

    private static TrackingStatus Map(ParcelStatus status) => status switch
    {
        ParcelStatus.Created => TrackingStatus.Created,
        ParcelStatus.PickedUp => TrackingStatus.PickedUp,
        ParcelStatus.InTransit => TrackingStatus.InTransit,
        ParcelStatus.AtSortingCenter => TrackingStatus.AtSortingCenter,
        ParcelStatus.OutForDelivery => TrackingStatus.OutForDelivery,
        ParcelStatus.Delivered => TrackingStatus.Delivered,
        ParcelStatus.Failed => TrackingStatus.Failed,
        ParcelStatus.Returned => TrackingStatus.Returned,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}

public sealed class GetPublicTrackingHandler(ITrackingStore store) : IRequestHandler<GetPublicTracking, PublicTrackingDto>
{
    public async Task<PublicTrackingDto> Handle(GetPublicTracking request, CancellationToken cancellationToken) =>
        (await store.FindByTrackingNumberAsync(TrackingRules.NormalizeTrackingNumber(request.TrackingNumber), cancellationToken)
            ?? throw new TrackingNotFoundException("Tracking number was not found.")).ToPublicDto();
}

public sealed class GetShipmentTrackingHandler(ITrackingStore store) : IRequestHandler<GetShipmentTracking, InternalTrackingDto>
{
    public async Task<InternalTrackingDto> Handle(GetShipmentTracking request, CancellationToken cancellationToken) =>
        (await store.FindByShipmentAsync(request.ShipmentId, cancellationToken)
            ?? throw new TrackingNotFoundException("Shipment tracking was not found.")).ToInternalDto();
}
