using System.Collections.Concurrent;
using FluentAssertions;
using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Application.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Delivery.Application;
using ParcelFlow.Delivery.Domain;
using ParcelFlow.Delivery.Infrastructure;
using ParcelFlow.Delivery.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Delivery.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceConsumerTests(InfrastructureFixture infrastructure)
{
    private DeliveryDbContext CreateContext() => new(new DbContextOptionsBuilder<DeliveryDbContext>().UseNpgsql(infrastructure.Postgres.GetConnectionString()).Options);
    private sealed class Actor : IDeliveryActor { public string Subject => "delivery-test-admin"; public bool IsAdmin => true; }
    private IHost CreateHost() => Host.CreateDefaultBuilder().ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DeliveryDatabase"] = infrastructure.Postgres.GetConnectionString(),
        ["ConnectionStrings:Redis"] = infrastructure.Redis.GetConnectionString(),
        ["RabbitMq:Host"] = infrastructure.RabbitMq.Hostname,
        ["RabbitMq:Port"] = infrastructure.RabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["RabbitMq:Username"] = "rabbitmq", ["RabbitMq:Password"] = "rabbitmq"
    })).ConfigureServices((context, services) =>
    {
        services.AddDeliveryApplication().AddDeliveryInfrastructure(context.Configuration);
        services.AddScoped<IDeliveryActor, Actor>();
    }).Build();
    private static async Task<T> Send<T>(IHost host, IRequest<T> command)
    {
        await using var scope = host.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }
    private static async Task<RouteDto> Route(IHost host, bool assignDriver = true)
    {
        var route = await Send(host, new CreateRoute(Guid.NewGuid(), "Tournée de test", new DateOnly(2026, 10, 18)));
        if (!assignDriver) return route;
        var subject = "driver-" + Guid.NewGuid().ToString("N"); await Send(host, new CreateDriver(subject, "Camille Martin"));
        return await Send(host, new AssignRouteDriver(route.Id, subject));
    }
    private static async Task<Guid> Parcel(IHost host)
    {
        var id = Guid.NewGuid(); await Send(host, new RegisterReadyShipment(id, PersistenceTests.Number())); await Send(host, new RegisterParcelPickup(id)); return id;
    }

    [Fact]
    public async Task Demo_seed_is_opt_in_idempotent_and_never_fabricates_proof_or_events()
    {
        using var host = CreateHost(); await host.Services.InitializeDeliveryDatabaseAsync();
        host.Services.GetRequiredService<IConfiguration>()["Database:SeedDemoData"] = "true";
        await host.Services.InitializeDeliveryDatabaseAsync(); await host.Services.InitializeDeliveryDatabaseAsync();
        await using var database = CreateContext();
        (await database.Drivers.CountAsync(x => x.Subject == "demo-driver-lyon" || x.Subject == "demo-driver-paris")).Should().Be(2);
        var route = await database.Routes.Include(x => x.Stops).SingleAsync(x => x.Id == Guid.Parse("60000000-0000-0000-0000-000000000001"));
        route.Status.Should().Be(DeliveryRouteStatus.Planned); route.Stops.Should().BeEmpty(); route.ServiceDate.Should().Be(new DateOnly(2026, 10, 18));
        (await database.Parcels.CountAsync(x => x.TrackingNumber == "PF600000000000000000000001" || x.TrackingNumber == "PF600000000000000000000002")).Should().Be(2);
        (await database.Attempts.AnyAsync(x => x.RouteId == route.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Pickup_before_registration_and_duplicate_transport_messages_preserve_parcel_state()
    {
        using var host = CreateHost(); await host.Services.InitializeDeliveryDatabaseAsync(); await host.StartAsync();
        try
        {
            var shipment = Guid.NewGuid(); var number = PersistenceTests.Number(); var bus = host.Services.GetRequiredService<IBus>();
            var pickup = new ParcelPickedUp(Guid.NewGuid(), shipment, DateTimeOffset.UtcNow, shipment);
            var first = Guid.NewGuid(); await bus.Publish(pickup, context => context.MessageId = first); await WaitForInbox(first);
            await using (var database = CreateContext()) { var pending = await database.Parcels.SingleAsync(x => x.ShipmentId == shipment); pending.TrackingNumber.Should().BeNull(); pending.IsPickedUp.Should().BeTrue(); }
            var ready = new ShipmentReadyForPickup(Guid.NewGuid(), shipment, DateTimeOffset.UtcNow, shipment, number);
            var second = Guid.NewGuid(); await bus.Publish(ready, context => context.MessageId = second); await WaitForInbox(second);
            var route = await Route(host); await Send(host, new AddRouteParcel(route.Id, shipment));
            foreach (var _ in Enumerable.Range(0, 2)) { var id = Guid.NewGuid(); await bus.Publish(pickup, context => context.MessageId = id); await WaitForInbox(id); }
            var third = Guid.NewGuid(); await bus.Publish(ready, context => context.MessageId = third); await WaitForInbox(third);
            await using var final = CreateContext(); var parcel = await final.Parcels.SingleAsync(x => x.ShipmentId == shipment);
            parcel.TrackingNumber.Should().Be(number); parcel.ActiveRouteId.Should().Be(route.Id); parcel.IsPickedUp.Should().BeTrue();
        }
        finally { await host.StopAsync(); }
    }

    [Fact]
    public async Task Twenty_concurrent_assignments_across_two_routes_reserve_one_parcel_once()
    {
        using var host = CreateHost(); await host.Services.InitializeDeliveryDatabaseAsync();
        var first = await Route(host); var second = await Route(host); var shipment = await Parcel(host);
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async index =>
        {
            try { await Send(host, new AddRouteParcel(index % 2 == 0 ? first.Id : second.Id, shipment)); return true; }
            catch (DeliveryConflictException) { return false; }
        }));
        results.Count(x => x).Should().Be(1); await using var database = CreateContext();
        (await database.Stops.CountAsync(x => x.ShipmentId == shipment)).Should().Be(1);
        new[] { first.Id, second.Id }.Should().Contain((await database.Parcels.SingleAsync(x => x.ShipmentId == shipment)).ActiveRouteId!.Value);
    }

    [Fact]
    public async Task Removing_planned_stop_releases_reservation_and_allows_reassignment()
    {
        using var host = CreateHost(); await host.Services.InitializeDeliveryDatabaseAsync();
        var route = await Route(host); var shipment = await Parcel(host); await Send(host, new AddRouteParcel(route.Id, shipment));
        await Send(host, new RemoveRouteParcel(route.Id, shipment));
        await using (var database = CreateContext()) { (await database.Stops.AnyAsync(x => x.RouteId == route.Id && x.ShipmentId == shipment)).Should().BeFalse(); }
        var restored = await Send(host, new AddRouteParcel(route.Id, shipment)); restored.Stops.Should().ContainSingle(x => x.ShipmentId == shipment);
    }

    [Fact]
    public async Task Driver_deactivation_racing_assignment_never_assigns_inactive_driver()
    {
        using var host = CreateHost(); await host.Services.InitializeDeliveryDatabaseAsync(); var route = await Route(host, false);
        var driver = await Send(host, new CreateDriver("driver-" + Guid.NewGuid().ToString("N"), "Alex Durand"));
        async Task<bool> Assign() { try { await Send(host, new AssignRouteDriver(route.Id, driver.Subject)); return true; } catch (DeliveryConflictException) { return false; } }
        async Task<bool> Deactivate() { try { await Send(host, new UpdateDriver(driver.Subject, driver.DisplayName, false)); return true; } catch (DeliveryConflictException) { return false; } }
        var results = await Task.WhenAll(Task.Run(Assign), Task.Run(Deactivate)); results.Count(x => x).Should().Be(1);
        await using var database = CreateContext(); var persisted = await database.Routes.SingleAsync(x => x.Id == route.Id);
        var persistedDriver = await database.Drivers.SingleAsync(x => x.Subject == driver.Subject);
        (persistedDriver.IsActive || persisted.DriverSubject == null).Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_outcomes_commit_one_proof_and_one_private_data_free_tracking_event()
    {
        using var host = CreateHost(); await host.Services.InitializeDeliveryDatabaseAsync(); await host.StartAsync();
        var route = await Route(host); var shipment = await Parcel(host); await Send(host, new AddRouteParcel(route.Id, shipment)); await Send(host, new StartRoute(route.Id));
        var observed = new ConcurrentBag<ParcelStatusRecorded>(); var bus = host.Services.GetRequiredService<IBus>();
        var observer = bus.ConnectReceiveEndpoint("delivery-observer-" + Guid.NewGuid().ToString("N"), endpoint => endpoint.Handler<ParcelStatusRecorded>(context =>
        { if (context.Message.ShipmentId == shipment && context.Message.Status != TrackingStatus.OutForDelivery) observed.Add(context.Message); return Task.CompletedTask; }));
        await observer.Ready;
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async index =>
            {
                try
                {
                    if (index % 2 == 0) await Send(host, new ConfirmDelivery(Guid.NewGuid(), route.Id, shipment, "Marie Dupont", PersistenceTests.Png(), null));
                    else await Send(host, new FailDelivery(Guid.NewGuid(), route.Id, shipment, DeliveryFailureReason.RecipientAbsent));
                    return true;
                }
                catch (DeliveryConflictException) { return false; }
            }));
            results.Count(x => x).Should().Be(1); await WaitUntil(() => observed.Count == 1);
            await using var database = CreateContext(); (await database.Attempts.CountAsync(x => x.RouteId == route.Id && x.ShipmentId == shipment)).Should().Be(1);
            (await database.Routes.SingleAsync(x => x.Id == route.Id)).Status.Should().Be(DeliveryRouteStatus.Completed);
            (await database.Parcels.SingleAsync(x => x.ShipmentId == shipment)).ActiveRouteId.Should().BeNull();
            var json = System.Text.Json.JsonSerializer.Serialize(observed.Single()); json.Should().NotContain("Marie").And.NotContain("Signature").And.NotContain("Photo").And.NotContain("Actor");
        }
        finally { await observer.StopAsync(); await host.StopAsync(); }
    }

    [Fact]
    public async Task Failure_after_mutation_rolls_back_proof_stop_parcel_and_outbox()
    {
        using var host = CreateHost(); await host.Services.InitializeDeliveryDatabaseAsync(); var route = await Route(host); var shipment = await Parcel(host);
        await Send(host, new AddRouteParcel(route.Id, shipment)); await Send(host, new StartRoute(route.Id));
        await using var database = CreateContext();
        await database.Database.ExecuteSqlRawAsync("ALTER TABLE delivery_attempts ADD CONSTRAINT test_delivery_rollback CHECK (\"ActorSubject\" <> 'delivery-test-admin') NOT VALID");
        try
        {
            var operation = Guid.NewGuid();
            await FluentActions.Awaiting(() => Send(host, new ConfirmDelivery(operation, route.Id, shipment, "Marie Dupont", PersistenceTests.Png(), null))).Should().ThrowAsync<DbUpdateException>();
            await using var verification = CreateContext(); var parcel = await verification.Parcels.SingleAsync(x => x.ShipmentId == shipment);
            parcel.IsDelivered.Should().BeFalse(); parcel.ActiveRouteId.Should().Be(route.Id);
            (await verification.Stops.SingleAsync(x => x.RouteId == route.Id && x.ShipmentId == shipment)).Status.Should().Be(DeliveryStopStatus.Pending);
            (await verification.Attempts.AnyAsync(x => x.OperationId == operation)).Should().BeFalse();
            (await verification.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().AnyAsync(x => x.MessageId == DeterministicGuid.Create("delivery:delivered:", shipment, operation))).Should().BeFalse();
        }
        finally { await database.Database.ExecuteSqlRawAsync("ALTER TABLE delivery_attempts DROP CONSTRAINT test_delivery_rollback"); }
    }
    [Fact]
    public async Task Committed_proof_replay_is_idempotent_and_changed_payload_conflicts()
    {
        using var host = CreateHost(); await host.Services.InitializeDeliveryDatabaseAsync(); var route = await Route(host); var shipment = await Parcel(host);
        await Send(host, new AddRouteParcel(route.Id, shipment)); await Send(host, new StartRoute(route.Id));
        var command = new ConfirmDelivery(Guid.NewGuid(), route.Id, shipment, "Marie Dupont", PersistenceTests.Png(), null);
        var first = await Send(host, command); var replay = await Send(host, command); replay.Should().Be(first);
        await FluentActions.Awaiting(() => Send(host, command with { RecipientName = "Autre destinataire" })).Should().ThrowAsync<DeliveryConflictException>();
        await using var database = CreateContext(); (await database.Attempts.CountAsync(x => x.OperationId == command.OperationId)).Should().Be(1);
        (await database.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().CountAsync(x => x.MessageId == DeterministicGuid.Create("delivery:delivered:", shipment, command.OperationId))).Should().Be(1);
    }

    [Fact]
    public async Task Driver_deactivation_racing_start_preserves_active_driver_on_open_route()
    {
        using var host = CreateHost(); await host.Services.InitializeDeliveryDatabaseAsync(); var route = await Route(host); var shipment = await Parcel(host);
        await Send(host, new AddRouteParcel(route.Id, shipment));
        async Task<bool> Start() { try { await Send(host, new StartRoute(route.Id)); return true; } catch (DeliveryConflictException) { return false; } }
        async Task<bool> Deactivate() { try { await Send(host, new UpdateDriver(route.DriverSubject!, "Camille Martin", false)); return true; } catch (DeliveryConflictException) { return false; } }
        var results = await Task.WhenAll(Task.Run(Start), Task.Run(Deactivate)); results.Should().Equal(true, false);
        await using var database = CreateContext(); (await database.Routes.SingleAsync(x => x.Id == route.Id)).Status.Should().Be(DeliveryRouteStatus.InProgress);
        (await database.Drivers.SingleAsync(x => x.Subject == route.DriverSubject)).IsActive.Should().BeTrue();
    }
    private async Task WaitForInbox(Guid id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true) { await using var database = CreateContext(); if (await database.Set<MassTransit.EntityFrameworkCoreIntegration.InboxState>().AnyAsync(x => x.MessageId == id && x.Delivered != null, timeout.Token)) return; await Task.Delay(100, timeout.Token); }
    }
    private static async Task WaitUntil(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); while (!predicate()) await Task.Delay(100, timeout.Token);
    }
}
