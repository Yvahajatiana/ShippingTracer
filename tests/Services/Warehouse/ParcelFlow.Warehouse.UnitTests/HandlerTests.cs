using System.Reflection;
using FluentAssertions;
using MassTransit;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Warehouse.Application;
using ParcelFlow.Warehouse.Domain;
using Xunit;

namespace ParcelFlow.Warehouse.UnitTests;

public sealed class HandlerTests
{
    private const string Number = "PF0123456789ABCDEF01234567";
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Ready_registration_is_idempotent_and_refuses_changed_identity()
    {
        var store = new Store(); var handler = new RegisterReadyShipmentHandler(store);
        var request = new RegisterReadyShipment(Guid.NewGuid(), Number.ToLowerInvariant());
        (await handler.Handle(request, default)).Should().BeTrue();
        (await handler.Handle(request with { TrackingNumber = Number }, default)).Should().BeFalse();
        await FluentActions.Awaiting(() => handler.Handle(request with { TrackingNumber = "PF111111111111111111111111" }, default)).Should().ThrowAsync<WarehouseConflictException>();
        await FluentActions.Awaiting(() => handler.Handle(request with { ShipmentId = Guid.NewGuid() }, default)).Should().ThrowAsync<WarehouseConflictException>();
        store.Inventories.Should().ContainSingle(); store.Saves.Should().Be(1);
    }
    [Fact]
    public async Task First_entry_publishes_pickup_once_and_replay_returns_original_scan()
    {
        var fixture = Create(); var request = fixture.Request;
        var first = await fixture.Handler.Handle(request, default);
        var duplicate = await fixture.Handler.Handle(request with { TrackingNumber = Number.ToLowerInvariant() }, default);
        duplicate.Should().Be(first); fixture.Store.Saves.Should().Be(1);
        fixture.Publisher.Events.Should().ContainSingle().Which.Should().BeOfType<ParcelPickedUp>()
            .Which.EventId.Should().Be(request.ScanId);
        fixture.Store.Scans.Should().ContainSingle();
        await FluentActions.Awaiting(() => fixture.Handler.Handle(request with { OperatorSubject = "other-operator" }, default)).Should().ThrowAsync<WarehouseConflictException>();
        await FluentActions.Awaiting(() => fixture.Handler.Handle(request with { Direction = ScanDirection.Exit }, default)).Should().ThrowAsync<WarehouseConflictException>();
        fixture.Publisher.Events.Should().ContainSingle(); fixture.Store.Saves.Should().Be(1);
    }
    [Fact]
    public async Task First_sorting_entry_publishes_distinct_pickup_and_sorting_facts()
    {
        var fixture = Create(WarehouseKind.SortingCenter);
        await fixture.Handler.Handle(fixture.Request, default);
        var pickup = fixture.Publisher.Events.OfType<ParcelPickedUp>().Single();
        var sorted = fixture.Publisher.Events.OfType<ParcelStatusRecorded>().Single();
        pickup.EventId.Should().NotBe(sorted.EventId); sorted.EventId.Should().Be(fixture.Request.ScanId);
        sorted.Status.Should().Be(TrackingStatus.AtSortingCenter); sorted.FacilityCode.Should().Be("PAR-01");
        pickup.OccurredAtUtc.Should().Be(sorted.OccurredAtUtc);
        pickup.CorrelationId.Should().Be(sorted.ShipmentId);
    }
    [Fact]
    public async Task Exit_and_subsequent_entry_publish_transit_without_new_pickup_and_time_is_monotone()
    {
        var fixture = Create();
        await fixture.Handler.Handle(fixture.Request, default);
        var exit = await fixture.Handler.Handle(fixture.Request with { ScanId = Guid.NewGuid(), Direction = ScanDirection.Exit }, default);
        var entry = await fixture.Handler.Handle(fixture.Request with { ScanId = Guid.NewGuid() }, default);
        exit.OccurredAtUtc.Should().Be(Start.AddTicks(10)); entry.OccurredAtUtc.Should().Be(Start.AddTicks(20));
        fixture.Publisher.Events.OfType<ParcelPickedUp>().Should().ContainSingle();
        fixture.Publisher.Events.OfType<ParcelStatusRecorded>().Should().HaveCount(2).And.OnlyContain(x => x.Status == TrackingStatus.InTransit);
    }
    [Fact]
    public async Task Unknown_or_unpaid_parcel_never_creates_stock_or_events()
    {
        var fixture = Create(); fixture.Store.Inventories.Clear();
        await FluentActions.Awaiting(() => fixture.Handler.Handle(fixture.Request, default)).Should().ThrowAsync<WarehouseConflictException>();
        fixture.Store.Scans.Should().BeEmpty(); fixture.Publisher.Events.Should().BeEmpty(); fixture.Store.Saves.Should().Be(0);
    }
    [Fact]
    public async Task Invalid_stock_movement_has_no_additional_persistence_or_events()
    {
        var fixture = Create();
        await fixture.Handler.Handle(fixture.Request, default);
        await FluentActions.Awaiting(() => fixture.Handler.Handle(fixture.Request with { ScanId = Guid.NewGuid() }, default)).Should().ThrowAsync<WarehouseConflictException>();
        fixture.Store.Scans.Should().ContainSingle(); fixture.Store.Saves.Should().Be(1); fixture.Publisher.Events.Should().ContainSingle();
    }
    [Fact]
    public async Task Outbox_publication_failure_prevents_save()
    {
        var fixture = Create(); fixture.Publisher.Failure = new InvalidOperationException("Outbox unavailable");
        await FluentActions.Awaiting(() => fixture.Handler.Handle(fixture.Request, default)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.Saves.Should().Be(0);
    }
    [Fact]
    public async Task Delivery_start_exits_parcel_in_stock_with_system_audit_and_no_tracking_event()
    {
        var fixture = Create(); await fixture.Handler.Handle(fixture.Request, default);
        var parcel = fixture.Store.Inventories.Single(); var site = fixture.Store.Warehouses.Single();
        var source = Guid.NewGuid(); var at = Start.AddMinutes(30);
        var handler = new RecordDeliveryExitHandler(fixture.Store);
        (await handler.Handle(new(source, parcel.ShipmentId, Number.ToLowerInvariant(), at), default)).Should().Be(DeliveryExitOutcome.Recorded);
        parcel.CurrentWarehouseId.Should().BeNull(); parcel.LastScanAtUtc.Should().Be(at);
        var exit = fixture.Store.Scans.Should().HaveCount(2).And.Subject.Last();
        exit.ScanId.Should().Be(WarehouseRules.DeliveryExitScanId(source)); exit.Direction.Should().Be(ScanDirection.Exit);
        exit.WarehouseId.Should().Be(site.Id); exit.OperatorSubject.Should().Be(WarehouseRules.DeliveryStartOperator);
        fixture.Publisher.Events.Should().ContainSingle().Which.Should().BeOfType<ParcelPickedUp>();
        fixture.Store.Saves.Should().Be(2);
    }
    [Fact]
    public async Task Delivery_start_redelivery_and_follow_up_statuses_never_move_parcel_twice()
    {
        var fixture = Create(); await fixture.Handler.Handle(fixture.Request, default);
        var parcel = fixture.Store.Inventories.Single(); var handler = new RecordDeliveryExitHandler(fixture.Store);
        var request = new RecordDeliveryExit(Guid.NewGuid(), parcel.ShipmentId, Number, Start.AddMinutes(30));
        (await handler.Handle(request, default)).Should().Be(DeliveryExitOutcome.Recorded);
        (await handler.Handle(request, default)).Should().Be(DeliveryExitOutcome.AlreadyRecorded);
        (await handler.Handle(request with { SourceEventId = Guid.NewGuid(), OccurredAtUtc = Start.AddHours(2) }, default)).Should().Be(DeliveryExitOutcome.NotInStock);
        fixture.Store.Scans.Should().HaveCount(2); fixture.Store.Saves.Should().Be(2);
    }
    [Fact]
    public async Task Delivery_start_ignores_parcels_never_in_stock_unknown_or_moved_after_the_event()
    {
        var fixture = Create(); var parcel = fixture.Store.Inventories.Single(); var handler = new RecordDeliveryExitHandler(fixture.Store);
        (await handler.Handle(new(Guid.NewGuid(), parcel.ShipmentId, Number, Start), default)).Should().Be(DeliveryExitOutcome.NotInStock);
        (await handler.Handle(new(Guid.NewGuid(), Guid.NewGuid(), Number, Start), default)).Should().Be(DeliveryExitOutcome.UnknownParcel);
        (await handler.Handle(new(Guid.NewGuid(), parcel.ShipmentId, "PF111111111111111111111111", Start), default)).Should().Be(DeliveryExitOutcome.UnknownParcel);
        await fixture.Handler.Handle(fixture.Request, default);
        (await handler.Handle(new(Guid.NewGuid(), parcel.ShipmentId, Number, Start.AddMinutes(-5)), default)).Should().Be(DeliveryExitOutcome.Superseded);
        parcel.CurrentWarehouseId.Should().NotBeNull(); fixture.Store.Scans.Should().ContainSingle(); fixture.Store.Saves.Should().Be(1);
    }
    [Fact]
    public async Task Redelivered_completion_event_does_not_undo_a_later_return_to_the_depot()
    {
        var fixture = Create(); await fixture.Handler.Handle(fixture.Request, default);
        var parcel = fixture.Store.Inventories.Single(); var handler = new RecordDeliveryExitHandler(fixture.Store);
        var failed = new RecordDeliveryExit(Guid.NewGuid(), parcel.ShipmentId, Number, Start.AddMinutes(30));
        (await handler.Handle(failed, default)).Should().Be(DeliveryExitOutcome.Recorded);
        await fixture.Handler.Handle(fixture.Request with { ScanId = Guid.NewGuid() }, default);
        (await handler.Handle(failed, default)).Should().Be(DeliveryExitOutcome.AlreadyRecorded);
        (await handler.Handle(failed with { SourceEventId = Guid.NewGuid() }, default)).Should().Be(DeliveryExitOutcome.Superseded);
        parcel.CurrentWarehouseId.Should().NotBeNull(); fixture.Store.Scans.Should().HaveCount(3);
    }
    [Fact]
    public void Delivery_exit_validation_and_scan_identity_are_deterministic()
    {
        new RecordDeliveryExitValidator().Validate(new RecordDeliveryExit(Guid.Empty, Guid.Empty, "bad", default)).Errors.Should().HaveCount(4);
        var source = Guid.NewGuid();
        var id = WarehouseRules.DeliveryExitScanId(source);
        id.Should().Be(WarehouseRules.DeliveryExitScanId(source)).And.NotBe(source).And.NotBe(WarehouseRules.DeliveryExitScanId(Guid.NewGuid()));
        id.ToString()[14].Should().Be('8');
        FluentActions.Invoking(() => WarehouseRules.DeliveryExitScanId(Guid.Empty)).Should().Throw<ArgumentException>();
    }
    [Fact]
    public async Task Occupied_site_cannot_be_deactivated_and_keeps_details()
    {
        var fixture = Create(); await fixture.Handler.Handle(fixture.Request, default);
        var site = fixture.Store.Warehouses.Single(); var handler = new UpdateWarehouseHandler(fixture.Store);
        await FluentActions.Awaiting(() => handler.Handle(new(site.Id, "Changed", "New street", "Lyon", "69002", "FR", false), default)).Should().ThrowAsync<WarehouseConflictException>();
        site.Name.Should().Be("Paris"); site.IsActive.Should().BeTrue(); fixture.Store.Saves.Should().Be(1);
    }
    [Fact]
    public async Task Missing_warehouse_queries_return_not_found()
    {
        var store = new Store();
        await FluentActions.Awaiting(() => new GetWarehouseHandler(store).Handle(new(Guid.NewGuid()), default)).Should().ThrowAsync<WarehouseNotFoundException>();
        await FluentActions.Awaiting(() => new GetWarehouseStockHandler(store).Handle(new(Guid.NewGuid()), default)).Should().ThrowAsync<WarehouseNotFoundException>();
    }
    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 101)]
    [InlineData(1_000_001, 1)]
    public void Pagination_is_bounded(int page, int pageSize) => new ListWarehousesValidator().Validate(new ListWarehouses(page, pageSize)).IsValid.Should().BeFalse();
    [Fact]
    public void Scan_validation_rejects_missing_ids_invalid_number_and_direction()
    {
        new ScanParcelValidator().Validate(new ScanParcel(Guid.Empty, Guid.Empty, "bad", (ScanDirection)99, " ")).Errors.Should().HaveCount(5);
        new CreateWarehouseValidator().Validate(new CreateWarehouse(Guid.NewGuid(), "PAR-01", "Paris", WarehouseKind.SortingCenter, "Street", "Paris", "75012", "FR")).IsValid.Should().BeTrue();
    }
    private static Fixture Create(WarehouseKind kind = WarehouseKind.Warehouse)
    {
        var store = new Store(); var site = DomainTests.Site(kind); store.AddWarehouse(site);
        store.AddInventory(new(Guid.NewGuid(), Number));
        var endpoint = DispatchProxy.Create<IPublishEndpoint, Publisher>();
        return new(store, (Publisher)endpoint, new(store, endpoint, new Clock()), new(Guid.NewGuid(), site.Id, Number, ScanDirection.Entry, "operator-42"));
    }
    private sealed record Fixture(Store Store, Publisher Publisher, ScanParcelHandler Handler, ScanParcel Request);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Start; }
    public class Publisher : DispatchProxy
    {
        public List<object> Events { get; } = [];
        public Exception? Failure { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (Failure is not null) return Task.FromException(Failure);
            Events.Add(args![0]!);
            return Task.CompletedTask;
        }
    }
    private sealed class Store : IWarehouseStore
    {
        public List<WarehouseSite> Warehouses { get; } = [];
        public List<ParcelInventory> Inventories { get; } = [];
        public List<WarehouseScan> Scans { get; } = [];
        public int Saves { get; private set; }
        public Task LockParcelAsync(string trackingNumber, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<WarehouseSite?> FindWarehouseAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Warehouses.SingleOrDefault(x => x.Id == id));
        public Task<ParcelInventory?> FindInventoryAsync(string number, CancellationToken cancellationToken) => Task.FromResult(Inventories.SingleOrDefault(x => x.TrackingNumber == number));
        public Task<ParcelInventory?> FindInventoryByShipmentAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Inventories.SingleOrDefault(x => x.ShipmentId == id));
        public Task<WarehouseScan?> FindScanAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Scans.SingleOrDefault(x => x.ScanId == id));
        public void AddWarehouse(WarehouseSite site) => Warehouses.Add(site);
        public void AddInventory(ParcelInventory parcel) => Inventories.Add(parcel);
        public void AddScan(WarehouseScan scan) => Scans.Add(scan);
        public Task SaveChangesAsync(CancellationToken cancellationToken) { Saves++; return Task.CompletedTask; }
        public Task<bool> HasStockAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Inventories.Any(x => x.CurrentWarehouseId == id));
        public Task<WarehousePageDto> ListWarehousesAsync(int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult(new WarehousePageDto(page, pageSize, Warehouses.Count, Warehouses.Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.ToDto()).ToArray()));
        public Task<WarehouseStockDto> GetStockAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken)
        {
            var items = Inventories.Where(x => x.CurrentWarehouseId == id).ToArray();
            return Task.FromResult(new WarehouseStockDto(id, page, pageSize, items.Length, items.Skip((page - 1) * pageSize).Take(pageSize).Select(x => new StockParcelDto(x.ShipmentId, x.TrackingNumber, x.LastScanAtUtc!.Value)).ToArray()));
        }
    }
}
