using FluentAssertions;
using FluentValidation;
using MassTransit;
using ParcelFlow.BuildingBlocks.Application.Identity;
using ParcelFlow.Shipment.Application;
using ParcelFlow.Shipment.Domain;
using System.Reflection;
using Xunit;

namespace ParcelFlow.Shipment.UnitTests;

public sealed class HandlerTests
{
    private static readonly AddressInput Address = new("Alice Martin", "alice@example.test", "+33612345678", "8 rue de Paris", null, "75001", "Paris", "FR");

    [Fact]
    public async Task Creation_uses_subject_tariff_and_stages_event_before_save()
    {
        var store = new Store();
        var publisher = DispatchProxy.Create<IPublishEndpoint, Publisher>();
        var events = (Publisher)(object)publisher;
        store.BeforeSave = () => events.Events.Should().ContainSingle();
        var handler = Handler(store, new User("alice", "customer"), new Labels(), publisher);
        var result = await handler.Handle(new CreateShipment(Address, Address, new(1.1m, 10, 10, 10)), default);
        result.OwnerSubject.Should().Be("alice");
        result.Price.Should().Be(8m);
        result.Status.Should().Be(ShipmentStatus.AwaitingPayment);
        var integrationEvent = events.Events.Single().Should().BeOfType<BuildingBlocks.Contracts.Shipment.V1.ShipmentCreated>().Subject;
        integrationEvent.CorrelationId.Should().Be(result.Id);
        integrationEvent.EventId.Should().Be(DeterministicGuid.Create("shipment:created:", result.Id));
        integrationEvent.CustomerSubject.Should().Be("alice");
        integrationEvent.Amount.Should().Be(8m);
        store.Saves.Should().Be(1);
    }

    [Fact]
    public async Task Quote_does_not_persist_shipment()
    {
        var store = new Store();
        var result = await Handler(store, new User("alice", "customer")).Handle(new QuoteShipment(Address, Address, new(1, 30, 20, 20)), default);
        result.Price.Should().Be(10m);
        result.BillableWeightKg.Should().Be(2.4m);
        store.Shipments.Should().BeEmpty();
        store.Saves.Should().Be(0);
    }

    [Fact]
    public async Task Label_failure_prevents_persistence_and_publication()
    {
        var store = new Store();
        var publisher = DispatchProxy.Create<IPublishEndpoint, Publisher>();
        var labels = new Labels { Failure = new ValidationException("Address contains unsupported glyphs.") };
        var handler = Handler(store, new User("alice", "customer"), labels, publisher);

        await FluentActions.Awaiting(() => handler.Handle(new CreateShipment(Address, Address, new(1, 10, 10, 10)), default))
            .Should().ThrowAsync<ValidationException>();

        labels.Calls.Should().Be(1);
        store.Shipments.Should().BeEmpty();
        ((Publisher)(object)publisher).Events.Should().BeEmpty();
        store.Saves.Should().Be(0);
    }

    [Fact]
    public async Task Customer_list_is_scoped_to_its_subject()
    {
        var store = new Store();
        await Handler(store, new User("alice", "customer")).Handle(new ListShipments(), default);
        store.OwnerFilter.Should().Be("alice");
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("operator")]
    public async Task Staff_can_list_and_label_other_customer_shipments(string role)
    {
        var store = new Store();
        var shipment = AddShipment(store, "alice");
        var labels = new Labels();
        var handler = Handler(store, new User("staff", role), labels);
        await handler.Handle(new ListShipments(), default);
        store.OwnerFilter.Should().BeNull();
        (await handler.Handle(new GetLabel(shipment.Id), default)).Should().Equal(1, 2, 3);
        labels.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Customer_cannot_read_or_generate_other_customer_label()
    {
        var store = new Store();
        var shipment = AddShipment(store, "bob");
        var labels = new Labels();
        var handler = Handler(store, new User("alice", "customer"), labels);
        await FluentActions.Awaiting(() => handler.Handle(new GetShipment(shipment.Id), default)).Should().ThrowAsync<ShipmentForbiddenException>();
        await FluentActions.Awaiting(() => handler.Handle(new GetLabel(shipment.Id), default)).Should().ThrowAsync<ShipmentForbiddenException>();
        labels.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Missing_shipment_returns_not_found() =>
        await FluentActions.Awaiting(() => Handler(new Store(), new User("alice", "customer")).Handle(new GetShipment(Guid.NewGuid()), default))
            .Should().ThrowAsync<ShipmentNotFoundException>();

    [Theory]
    [InlineData("customer")]
    [InlineData("operator")]
    public async Task Only_admin_can_change_tariff(string role)
    {
        var store = new Store();
        await FluentActions.Awaiting(() => Handler(store, new User("alice", role)).Handle(new UpdateTariff(DeliveryZone.Domestic, 1m, 1m), default))
            .Should().ThrowAsync<ShipmentForbiddenException>();
        store.Saves.Should().Be(0);
    }

    [Fact]
    public async Task Admin_changes_tariff()
    {
        var store = new Store();
        (await Handler(store, new User("admin", "admin")).Handle(new UpdateTariff(DeliveryZone.Domestic, 1m, 1m), default)).BasePrice.Should().Be(1m);
        store.Saves.Should().Be(1);
    }

    [Theory]
    [InlineData("", "customer")]
    [InlineData("anonymous", "unknown")]
    public async Task Missing_identity_or_role_is_rejected(string subject, string role) =>
        await FluentActions.Awaiting(() => Handler(new Store(), new User(subject, role)).Handle(new ListShipments(), default))
            .Should().ThrowAsync<ShipmentForbiddenException>();

    private static ShipmentHandlers Handler(Store store, User user, Labels? labels = null, IPublishEndpoint? publisher = null) =>
        new(store, user, new Numbers(), labels ?? new Labels(), publisher!, new ShipmentAccess(store, user));
    private static ParcelShipment AddShipment(Store store, string owner)
    {
        var shipment = new ParcelShipment(owner, new Numbers().Generate(), Address.ToDomain(), Address.ToDomain(), new(1, 10, 10, 10), 6m, DeliveryZone.Domestic);
        store.AddShipment(shipment);
        return shipment;
    }
    private sealed record User(string Subject, string Role) : ICurrentUser { public bool IsInRole(string role) => Role == role; }
    private sealed class Numbers : ITrackingNumberGenerator { public string Generate() => "PF0123456789ABCDEF01234567"; }
    private sealed class Labels : IShippingLabelGenerator
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }
        public byte[] Generate(ParcelShipment shipment)
        {
            Calls++;
            if (Failure is not null) throw Failure;
            return [1, 2, 3];
        }
    }
    public class Publisher : DispatchProxy
    {
        public List<object> Events { get; } = [];
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Events.Add(args![0]!);
            return Task.CompletedTask;
        }
    }
    private sealed class Store : IShipmentStore
    {
        public List<ParcelShipment> Shipments { get; } = [];
        public Tariff Tariff { get; } = new(DeliveryZone.Domestic, 4m, 2m);
        public string? OwnerFilter { get; private set; }
        public int Saves { get; private set; }
        public Action? BeforeSave { get; set; }
        public void AddShipment(ParcelShipment shipment) => Shipments.Add(shipment);
        public Task<ParcelShipment?> FindShipmentAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Shipments.Find(x => x.Id == id));
        public Task<IReadOnlyList<ParcelShipment>> ListShipmentsAsync(string? ownerSubject, int offset, int limit, CancellationToken cancellationToken)
        {
            OwnerFilter = ownerSubject;
            return Task.FromResult<IReadOnlyList<ParcelShipment>>(Shipments.Where(x => ownerSubject is null || x.OwnerSubject == ownerSubject).Skip(offset).Take(limit).ToArray());
        }
        public Task<Tariff?> FindTariffAsync(DeliveryZone zone, CancellationToken cancellationToken) => Task.FromResult<Tariff?>(zone == Tariff.Zone ? Tariff : null);
        public Task<IReadOnlyList<Tariff>> ListTariffsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Tariff>>([Tariff]);
        public Task SaveChangesAsync(CancellationToken cancellationToken) { BeforeSave?.Invoke(); Saves++; return Task.CompletedTask; }
    }
}
