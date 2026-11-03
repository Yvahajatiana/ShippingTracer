using System.Reflection;
using FluentAssertions;
using MassTransit;
using ParcelFlow.BuildingBlocks.Application.Identity;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Delivery.Application;
using ParcelFlow.Delivery.Domain;
using Xunit;

namespace ParcelFlow.Delivery.UnitTests;

public sealed class DeliveryTests
{
    private const string Number = "PF0123456789ABCDEF01234567";
    private static readonly byte[] Signature = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGNgAAIAAAUAAXpeqz8AAAAASUVORK5CYII=");
    [Fact]
    public async Task Pickup_before_ready_is_retained_and_both_events_are_idempotent()
    {
        var store = new Store(); var id = Guid.NewGuid();
        var pickup = new RegisterParcelPickupHandler(store); var ready = new RegisterReadyShipmentHandler(store);
        (await pickup.Handle(new(id), default)).Should().BeTrue();
        (await ready.Handle(new(id, Number.ToLowerInvariant()), default)).Should().BeTrue();
        (await pickup.Handle(new(id), default)).Should().BeFalse();
        (await ready.Handle(new(id, Number), default)).Should().BeFalse();
        store.Parcels.Single().TrackingNumber.Should().Be(Number); store.Saves.Should().Be(2);
        await FluentActions.Awaiting(() => ready.Handle(new(id, "PF111111111111111111111111"), default)).Should().ThrowAsync<DeliveryConflictException>();
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Assignment_requires_ready_and_pickup(bool ready, bool pickup)
    {
        var parcel = new DeliveryParcel(Guid.NewGuid()); if (ready) parcel.Register(Number); if (pickup) parcel.MarkPickedUp();
        FluentActions.Invoking(() => parcel.Assign(Guid.NewGuid())).Should().Throw<InvalidOperationException>();
    }
    [Fact]
    public void A_parcel_cannot_belong_to_two_open_routes_and_planned_removal_releases_it()
    {
        var parcel = Parcel(); var first = Route(); var second = Route();
        first.AddParcel(parcel);
        FluentActions.Invoking(() => second.AddParcel(parcel)).Should().Throw<InvalidOperationException>();
        first.RemoveParcel(parcel); second.AddParcel(parcel); first.Stops.Should().BeEmpty(); parcel.ActiveRouteId.Should().Be(second.Id);
    }
    [Fact]
    public void Active_routes_are_immutable_and_need_active_driver_and_parcels()
    {
        var driver = new DeliveryDriver("driver", "Alice"); var route = Route(); route.AssignDriver(driver);
        FluentActions.Invoking(() => route.Start(driver, DateTimeOffset.UtcNow)).Should().Throw<InvalidOperationException>();
        route.AddParcel(Parcel()); driver.Update("Alice", false);
        FluentActions.Invoking(() => route.Start(driver, DateTimeOffset.UtcNow)).Should().Throw<InvalidOperationException>();
        driver.Update("Alice", true); route.Start(driver, DateTimeOffset.UtcNow);
        FluentActions.Invoking(() => route.AddParcel(Parcel())).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => route.AssignDriver(driver)).Should().Throw<InvalidOperationException>();
    }
    [Fact]
    public async Task Assigned_driver_starts_route_and_publishes_only_public_logistics_facts()
    {
        var f = Fixture(); var dto = await new StartRouteHandler(f.Store, new Actor("driver"), f.Endpoint, new Clock()).Handle(new(f.Route.Id), default);
        dto.Status.Should().Be(DeliveryRouteStatus.InProgress); f.Publisher.Events.Should().ContainSingle();
        var fact = f.Publisher.Events.Single().Should().BeOfType<ParcelStatusRecorded>().Which;
        fact.Status.Should().Be(TrackingStatus.OutForDelivery); fact.FacilityCode.Should().BeNull(); fact.TrackingNumber.Should().Be(Number); f.Store.Saves.Should().Be(1);
        // One departure per stop: the EventId derives from the route and the shipment, never from a random value.
        fact.EventId.Should().Be(DeterministicGuid.Create("delivery:out-for-delivery:", f.Route.Id, fact.ShipmentId));
        fact.CorrelationId.Should().Be(fact.ShipmentId);
    }
    [Fact]
    public async Task Another_operator_cannot_start_complete_or_read_proof()
    {
        var f = Fixture(); var actor = new Actor("other");
        await FluentActions.Awaiting(() => new StartRouteHandler(f.Store, actor, f.Endpoint, new Clock()).Handle(new(f.Route.Id), default)).Should().ThrowAsync<DeliveryForbiddenException>();
        await FluentActions.Awaiting(() => new ConfirmDeliveryHandler(f.Store, actor, f.Endpoint, new Clock()).Handle(new(Guid.NewGuid(), f.Route.Id, f.Parcel.ShipmentId, "Alice", Signature, null), default)).Should().ThrowAsync<DeliveryForbiddenException>();
        await FluentActions.Awaiting(() => new GetDeliveryProofHandler(f.Store, actor).Handle(new(f.Route.Id, f.Parcel.ShipmentId), default)).Should().ThrowAsync<DeliveryForbiddenException>();
        f.Store.Saves.Should().Be(0); f.Publisher.Events.Should().BeEmpty();
    }
    [Fact]
    public async Task Successful_proof_is_private_idempotent_and_terminal()
    {
        var f = Fixture(); await Start(f); var handler = new ConfirmDeliveryHandler(f.Store, new Actor("driver"), f.Endpoint, new Clock());
        var request = new ConfirmDelivery(Guid.NewGuid(), f.Route.Id, f.Parcel.ShipmentId, " Alice ", Signature.ToArray(), null);
        var original = await handler.Handle(request, default); var replay = await handler.Handle(request with { SignaturePng = Signature.ToArray() }, default);
        replay.Should().Be(original); original.RecipientName.Should().Be("Alice"); original.HasSignature.Should().BeTrue(); original.HasPhoto.Should().BeFalse();
        f.Store.Attempts.Should().ContainSingle(); f.Store.Saves.Should().Be(2); f.Publisher.Events.Should().HaveCount(2);
        f.Route.Status.Should().Be(DeliveryRouteStatus.Completed); f.Parcel.IsDelivered.Should().BeTrue(); f.Parcel.ActiveRouteId.Should().BeNull();
        FluentActions.Invoking(() => f.Parcel.Assign(Guid.NewGuid())).Should().Throw<InvalidOperationException>();
        var proof = await new GetDeliveryProofImageHandler(f.Store, new Actor("admin", true)).Handle(new(f.Route.Id, f.Parcel.ShipmentId, ProofImageKind.Signature), default);
        proof.Content.Should().Equal(Signature); proof.ContentType.Should().Be("image/png");
        await FluentActions.Awaiting(() => handler.Handle(request with { RecipientName = "Bob" }, default)).Should().ThrowAsync<DeliveryConflictException>();
        await FluentActions.Awaiting(() => handler.Handle(request with { SignaturePng = Signature.Append((byte)0).ToArray() }, default)).Should().ThrowAsync<DeliveryConflictException>();
    }
    [Fact]
    public async Task Failed_stop_is_replannable_and_old_replay_does_not_change_new_assignment()
    {
        var f = Fixture(); await Start(f); var handler = new FailDeliveryHandler(f.Store, new Actor("driver"), f.Endpoint, new Clock());
        var request = new FailDelivery(Guid.NewGuid(), f.Route.Id, f.Parcel.ShipmentId, DeliveryFailureReason.RecipientAbsent);
        var failed = await handler.Handle(request, default); f.Parcel.IsDelivered.Should().BeFalse(); f.Parcel.ActiveRouteId.Should().BeNull();
        var next = Route(); next.AddParcel(f.Parcel); var pointer = f.Parcel.ActiveRouteId;
        (await handler.Handle(request, default)).Should().Be(failed); f.Parcel.ActiveRouteId.Should().Be(pointer);
        f.Publisher.Events.OfType<ParcelStatusRecorded>().Last().Status.Should().Be(TrackingStatus.Failed);
        await FluentActions.Awaiting(() => handler.Handle(request with { Reason = DeliveryFailureReason.RecipientRefused }, default)).Should().ThrowAsync<DeliveryConflictException>();
    }
    [Fact]
    public async Task One_outcome_per_stop_and_completion_waits_for_all_stops()
    {
        var f = Fixture(); var other = Parcel(); f.Store.Parcels.Add(other); f.Route.AddParcel(other); await Start(f);
        var handler = new FailDeliveryHandler(f.Store, new Actor("driver"), f.Endpoint, new Clock());
        await handler.Handle(new(Guid.NewGuid(), f.Route.Id, f.Parcel.ShipmentId, DeliveryFailureReason.AddressNotFound), default);
        f.Route.Status.Should().Be(DeliveryRouteStatus.InProgress);
        await FluentActions.Awaiting(() => handler.Handle(new(Guid.NewGuid(), f.Route.Id, f.Parcel.ShipmentId, DeliveryFailureReason.AddressNotFound), default)).Should().ThrowAsync<DeliveryConflictException>();
        await handler.Handle(new(Guid.NewGuid(), f.Route.Id, other.ShipmentId, DeliveryFailureReason.RecipientAbsent), default);
        f.Route.Status.Should().Be(DeliveryRouteStatus.Completed);
    }
    [Fact]
    public async Task Outbox_failure_prevents_save()
    {
        var f = Fixture(); f.Publisher.Failure = new InvalidOperationException("Outbox unavailable");
        await FluentActions.Awaiting(() => Start(f)).Should().ThrowAsync<InvalidOperationException>(); f.Store.Saves.Should().Be(0);
    }
    [Fact]
    public async Task Driver_with_open_route_cannot_be_disabled()
    {
        var f = Fixture(); await FluentActions.Awaiting(() => new UpdateDriverHandler(f.Store).Handle(new("driver", "Changed", false), default)).Should().ThrowAsync<DeliveryConflictException>();
        f.Store.Drivers.Single().DisplayName.Should().Be("Alice"); f.Store.Saves.Should().Be(0);
    }
    [Fact]
    public void Parcel_timestamps_are_utc_microseconds_and_strictly_monotonic()
    {
        var parcel = Parcel(); var time = new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.FromHours(2)).AddTicks(7);
        var first = parcel.RecordEvent(time); var second = parcel.RecordEvent(time.AddDays(-1));
        first.Offset.Should().Be(TimeSpan.Zero); (first.Ticks % 10).Should().Be(0); second.Should().Be(first.AddTicks(10));
    }
    [Fact]
    public void Invalid_proofs_and_control_characters_are_rejected()
    {
        var valid = new ConfirmDelivery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Alice", Signature, null);
        var validator = new ConfirmDeliveryValidator(); validator.Validate(valid).IsValid.Should().BeTrue();
        validator.Validate(valid with { SignaturePng = null }).IsValid.Should().BeFalse();
        validator.Validate(valid with { SignaturePng = [1, 2, 3] }).IsValid.Should().BeFalse();
        validator.Validate(valid with { RecipientName = "Alice\nInjected" }).IsValid.Should().BeFalse();
        validator.Validate(valid with { Photo = [] }).IsValid.Should().BeFalse();
        new FailDeliveryValidator().Validate(new FailDelivery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (DeliveryFailureReason)99)).IsValid.Should().BeFalse();
    }
    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 101)]
    [InlineData(1000001, 1)]
    public void Pagination_is_bounded(int page, int size)
    {
        new ListRoutesValidator().Validate(new ListRoutes(page, size)).IsValid.Should().BeFalse(); new ListDriversValidator().Validate(new ListDrivers(page, size)).IsValid.Should().BeFalse(); new ListAvailableParcelsValidator().Validate(new ListAvailableParcels(page, size)).IsValid.Should().BeFalse();
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Retried_outcome_operation_keeps_its_event_id_while_another_shipment_gets_a_different_one(bool delivered)
    {
        var operation = Guid.NewGuid(); var shipmentId = Guid.NewGuid();
        // Each run starts from the committed state, as a retry does after the first transaction rolled back.
        async Task<Guid> Complete(Guid shipment)
        {
            var f = Fixture(shipment); await Start(f); f.Publisher.Events.Clear();
            var actor = new Actor("driver");
            if (delivered) await new ConfirmDeliveryHandler(f.Store, actor, f.Endpoint, new Clock()).Handle(new(operation, f.Route.Id, shipment, "Alice", Signature, null), default);
            else await new FailDeliveryHandler(f.Store, actor, f.Endpoint, new Clock()).Handle(new(operation, f.Route.Id, shipment, DeliveryFailureReason.RecipientAbsent), default);
            return f.Publisher.Events.OfType<ParcelStatusRecorded>().Single().EventId;
        }
        var first = await Complete(shipmentId);
        first.Should().Be(DeterministicGuid.Create(delivered ? "delivery:delivered:" : "delivery:failed:", shipmentId, operation)).And.NotBe(operation);
        (await Complete(shipmentId)).Should().Be(first);
        // The same client key on another shipment never yields the same EventId (Inbox and consumer deduplication).
        (await Complete(Guid.NewGuid())).Should().NotBe(first);
    }
    private static DeliveryParcel Parcel() { var parcel = new DeliveryParcel(Guid.NewGuid()); parcel.Register(Number); parcel.MarkPickedUp(); return parcel; }
    private static DeliveryRoute Route() => new(Guid.NewGuid(), "Paris matin", new DateOnly(2026, 10, 18));
    private static F Fixture() => Fixture(Guid.NewGuid());
    private static F Fixture(Guid shipmentId)
    {
        var store = new Store(); var route = Route(); var parcel = new DeliveryParcel(shipmentId); parcel.Register(Number); parcel.MarkPickedUp(); var driver = new DeliveryDriver("driver", "Alice"); route.AssignDriver(driver); route.AddParcel(parcel);
        store.Routes.Add(route); store.Parcels.Add(parcel); store.Drivers.Add(driver); var endpoint = DispatchProxy.Create<IPublishEndpoint, Publisher>(); return new(store, route, parcel, endpoint, (Publisher)endpoint);
    }
    private static Task<RouteDto> Start(F f) => new StartRouteHandler(f.Store, new Actor("driver"), f.Endpoint, new Clock()).Handle(new(f.Route.Id), default);
    private sealed record F(Store Store, DeliveryRoute Route, DeliveryParcel Parcel, IPublishEndpoint Endpoint, Publisher Publisher);
    private sealed record Actor(string Subject, bool IsAdmin = false) : IDeliveryActor;
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 18, 10, 0, 0, TimeSpan.Zero); }
    public class Publisher : DispatchProxy
    {
        public List<object> Events { get; } = []; public Exception? Failure { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) { if (Failure is not null) return Task.FromException(Failure); Events.Add(args![0]!); return Task.CompletedTask; }
    }
    private sealed class Store : IDeliveryStore
    {
        public List<DeliveryDriver> Drivers { get; } = []; public List<DeliveryRoute> Routes { get; } = []; public List<DeliveryParcel> Parcels { get; } = []; public List<DeliveryAttempt> Attempts { get; } = []; public int Saves { get; private set; }
        public Task LockRouteAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task LockParcelAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task LockDriverAsync(string subject, CancellationToken ct) => Task.CompletedTask;
        public Task<DeliveryRoute?> FindRouteAsync(Guid id, CancellationToken ct) => Task.FromResult(Routes.SingleOrDefault(x => x.Id == id));
        public Task<DeliveryParcel?> FindParcelAsync(Guid id, CancellationToken ct) => Task.FromResult(Parcels.SingleOrDefault(x => x.ShipmentId == id));
        public Task<DeliveryDriver?> FindDriverAsync(string subject, CancellationToken ct) => Task.FromResult(Drivers.SingleOrDefault(x => x.Subject == subject));
        public Task<DeliveryAttempt?> FindAttemptAsync(Guid id, CancellationToken ct) => Task.FromResult(Attempts.SingleOrDefault(x => x.OperationId == id));
        public Task<DeliveryAttempt?> FindAttemptByStopAsync(Guid routeId, Guid shipmentId, CancellationToken ct) => Task.FromResult(Attempts.SingleOrDefault(x => x.RouteId == routeId && x.ShipmentId == shipmentId));
        public Task<bool> HasOpenRoutesAsync(string subject, CancellationToken ct) => Task.FromResult(Routes.Any(x => x.DriverSubject == subject && x.Status != DeliveryRouteStatus.Completed));
        public void AddDriver(DeliveryDriver value) => Drivers.Add(value); public void AddRoute(DeliveryRoute value) => Routes.Add(value); public void AddParcel(DeliveryParcel value) => Parcels.Add(value); public void AddAttempt(DeliveryAttempt value) => Attempts.Add(value);
        public Task SaveChangesAsync(CancellationToken ct) { Saves++; return Task.CompletedTask; }
        public Task<RoutePageDto> ListRoutesAsync(int page, int pageSize, CancellationToken ct) => Task.FromResult(new RoutePageDto(page, pageSize, Routes.Count, Routes.Select(x => x.ToDto()).ToArray()));
        public Task<DriverPageDto> ListDriversAsync(int page, int pageSize, CancellationToken ct) => Task.FromResult(new DriverPageDto(page, pageSize, Drivers.Count, Drivers.Select(x => x.ToDto()).ToArray()));
        public Task<AvailableParcelPageDto> ListAvailableParcelsAsync(int page, int pageSize, CancellationToken ct) => Task.FromResult(new AvailableParcelPageDto(page, pageSize, 0, []));
    }
}
