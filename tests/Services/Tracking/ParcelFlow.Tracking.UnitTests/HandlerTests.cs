using System.Reflection;
using FluentAssertions;
using MassTransit;
using ParcelFlow.BuildingBlocks.Application.Correlation;
using ParcelFlow.BuildingBlocks.Application.Identity;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Tracking.Application;
using ParcelFlow.Tracking.Domain;
using Xunit;

namespace ParcelFlow.Tracking.UnitTests;

public sealed class HandlerTests
{
    private const string Number = "PF0123456789ABCDEF01234567";
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Pending_fact_is_published_with_creation_when_number_becomes_known()
    {
        var store = new Store();
        var endpoint = DispatchProxy.Create<IPublishEndpoint, Publisher>();
        var events = ((Publisher)endpoint).Events;
        var flow = Guid.NewGuid();
        var handler = new RecordTrackingEventHandler(store, endpoint, CorrelationContext.From(flow));
        var shipmentId = Guid.NewGuid();
        var pickupId = Guid.NewGuid();
        var createdId = Guid.NewGuid();
        await handler.Handle(new(pickupId, shipmentId, null, ParcelStatus.PickedUp, Start.AddHours(1), null), default);
        events.Should().BeEmpty();
        store.Records.Single().TrackingNumber.Should().BeNull();
        await handler.Handle(new(createdId, shipmentId, Number, ParcelStatus.Created, Start, null), default);
        events.Select(x => x.SourceEventId).Should().Equal(createdId, pickupId);
        events.Should().OnlyContain(x => x.TrackingNumber == Number && x.CorrelationId == flow && x.ShipmentId == shipmentId);
        events.Select(x => x.EventId).Should().Equal(DeterministicGuid.Create("tracking:status-changed:", createdId),
            DeterministicGuid.Create("tracking:status-changed:", pickupId));
        store.Records.Single().CurrentStatus.Should().Be(ParcelStatus.PickedUp);
        store.Saves.Should().Be(2);
        await handler.Handle(new(createdId, shipmentId, Number, ParcelStatus.Created, Start, null), default);
        events.Should().HaveCount(2);
        store.Saves.Should().Be(2);
    }

    [Fact]
    public async Task Duplicate_pending_fact_can_register_number_and_publish_once()
    {
        var store = new Store();
        var endpoint = DispatchProxy.Create<IPublishEndpoint, Publisher>();
        var handler = new RecordTrackingEventHandler(store, endpoint, new CorrelationContext());
        var request = new RecordTrackingEvent(Guid.NewGuid(), Guid.NewGuid(), null, ParcelStatus.PickedUp, Start, null);
        (await handler.Handle(request, default)).Should().BeTrue();
        (await handler.Handle(request with { TrackingNumber = Number }, default)).Should().BeFalse();
        ((Publisher)endpoint).Events.Should().ContainSingle();
        store.Saves.Should().Be(2);
    }

    [Fact]
    public async Task Conflicting_duplicate_returns_conflict_and_does_not_publish()
    {
        var store = new Store();
        var endpoint = DispatchProxy.Create<IPublishEndpoint, Publisher>();
        var handler = new RecordTrackingEventHandler(store, endpoint, new CorrelationContext());
        var request = new RecordTrackingEvent(Guid.NewGuid(), Guid.NewGuid(), Number, ParcelStatus.Created, Start, null);
        await handler.Handle(request, default);
        await FluentActions.Awaiting(() => handler.Handle(request with { Status = ParcelStatus.Delivered }, default))
            .Should().ThrowAsync<TrackingConflictException>();
        ((Publisher)endpoint).Events.Should().ContainSingle();
        store.Saves.Should().Be(1);
    }

    [Fact]
    public async Task Queries_normalize_number_and_return_not_found_for_missing_tracking()
    {
        var store = new Store();
        var record = new TrackingRecord(Guid.NewGuid());
        record.Register(Number);
        record.AddEntry(new(Guid.NewGuid(), ParcelStatus.Created, Start, null));
        store.Add(record);
        (await new GetPublicTrackingHandler(store).Handle(new(" " + Number.ToLowerInvariant() + " "), default)).TrackingNumber.Should().Be(Number);
        (await new GetShipmentTrackingHandler(store).Handle(new(record.ShipmentId), default)).ShipmentId.Should().Be(record.ShipmentId);
        await FluentActions.Awaiting(() => new GetPublicTrackingHandler(new Store()).Handle(new(Number), default)).Should().ThrowAsync<TrackingNotFoundException>();
        await FluentActions.Awaiting(() => new GetShipmentTrackingHandler(store).Handle(new(Guid.NewGuid()), default)).Should().ThrowAsync<TrackingNotFoundException>();
    }

    [Fact]
    public async Task Publication_failure_does_not_save_the_tracking_fact()
    {
        var store = new Store();
        var endpoint = DispatchProxy.Create<IPublishEndpoint, Publisher>();
        ((Publisher)endpoint).Failure = new InvalidOperationException("Outbox is unavailable.");
        var handler = new RecordTrackingEventHandler(store, endpoint, new CorrelationContext());
        await FluentActions.Awaiting(() => handler.Handle(new(Guid.NewGuid(), Guid.NewGuid(), Number,
            ParcelStatus.Created, Start, null), default)).Should().ThrowAsync<InvalidOperationException>();
        store.Saves.Should().Be(0);
    }

    [Theory]
    [InlineData(ParcelStatus.Created, TrackingStatus.Created)]
    [InlineData(ParcelStatus.PickedUp, TrackingStatus.PickedUp)]
    [InlineData(ParcelStatus.InTransit, TrackingStatus.InTransit)]
    [InlineData(ParcelStatus.AtSortingCenter, TrackingStatus.AtSortingCenter)]
    [InlineData(ParcelStatus.OutForDelivery, TrackingStatus.OutForDelivery)]
    [InlineData(ParcelStatus.Delivered, TrackingStatus.Delivered)]
    [InlineData(ParcelStatus.Failed, TrackingStatus.Failed)]
    [InlineData(ParcelStatus.Returned, TrackingStatus.Returned)]
    public async Task Every_status_maps_to_the_versioned_integration_contract(ParcelStatus status, TrackingStatus expected)
    {
        var endpoint = DispatchProxy.Create<IPublishEndpoint, Publisher>();
        await new RecordTrackingEventHandler(new Store(), endpoint, new CorrelationContext()).Handle(new(Guid.NewGuid(), Guid.NewGuid(), Number, status, Start, "PARIS-01"), default);
        var published = ((Publisher)endpoint).Events.Single();
        published.Status.Should().Be(expected);
        published.OccurredAtUtc.Should().Be(Start);
        published.FacilityCode.Should().Be("PARIS-01");
    }

    public class Publisher : DispatchProxy
    {
        public List<TrackingStatusChanged> Events { get; } = [];
        public Exception? Failure { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (Failure is not null) return Task.FromException(Failure);
            Events.Add((TrackingStatusChanged)args![0]!);
            return Task.CompletedTask;
        }
    }
    private sealed class Store : ITrackingStore
    {
        public List<TrackingRecord> Records { get; } = [];
        public int Saves { get; private set; }
        public void Add(TrackingRecord record) => Records.Add(record);
        public Task<TrackingRecord?> FindByShipmentAsync(Guid shipmentId, CancellationToken cancellationToken) => Task.FromResult(Records.SingleOrDefault(x => x.ShipmentId == shipmentId));
        public Task<TrackingRecord?> FindByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken) => Task.FromResult(Records.SingleOrDefault(x => x.TrackingNumber == trackingNumber));
        public Task SaveChangesAsync(CancellationToken cancellationToken) { Saves++; return Task.CompletedTask; }
    }
}
