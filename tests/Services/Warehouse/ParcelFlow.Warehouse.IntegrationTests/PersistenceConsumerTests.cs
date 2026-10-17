using System.Collections.Concurrent;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.Warehouse.Application;
using ParcelFlow.Warehouse.Domain;
using ParcelFlow.Warehouse.Infrastructure;
using ParcelFlow.Warehouse.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Warehouse.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceConsumerTests(InfrastructureFixture infrastructure)
{
    private WarehouseDbContext CreateContext() => new(new DbContextOptionsBuilder<WarehouseDbContext>()
        .UseNpgsql(infrastructure.Postgres.GetConnectionString()).Options);
    private IHost CreateHost() => Host.CreateDefaultBuilder().ConfigureAppConfiguration(configuration =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WarehouseDatabase"] = infrastructure.Postgres.GetConnectionString(),
            ["ConnectionStrings:Redis"] = infrastructure.Redis.GetConnectionString(),
            ["RabbitMq:Host"] = infrastructure.RabbitMq.Hostname,
            ["RabbitMq:Port"] = infrastructure.RabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:Username"] = "rabbitmq", ["RabbitMq:Password"] = "rabbitmq"
        })).ConfigureServices((context, services) =>
        services.AddWarehouseApplication().AddWarehouseInfrastructure(context.Configuration)).Build();

    private static async Task<T> Send<T>(IHost host, IRequest<T> request)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
    private static async Task<WarehouseDto> CreateSite(IHost host) => await Send(host,
        new CreateWarehouse(Guid.NewGuid(), "FR-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Site de test", WarehouseKind.Warehouse,
            "18 avenue Tony Garnier", "Lyon", "69007", "FR"));

    [Fact]
    public async Task Demo_seed_is_explicit_and_idempotent()
    {
        using var host = CreateHost();
        await host.Services.InitializeWarehouseDatabaseAsync();
        host.Services.GetRequiredService<IConfiguration>()["Database:SeedDemoData"] = "true";
        await host.Services.InitializeWarehouseDatabaseAsync();
        await host.Services.InitializeWarehouseDatabaseAsync();
        await using var database = CreateContext();
        (await database.Warehouses.CountAsync(x => x.Id == Guid.Parse("50000000-0000-0000-0000-000000000001") || x.Id == Guid.Parse("50000000-0000-0000-0000-000000000002"))).Should().Be(2);
        (await database.Inventory.CountAsync(x => x.TrackingNumber == "PF500000000000000000000001" || x.TrackingNumber == "PF500000000000000000000002")).Should().Be(2);
    }

    [Fact]
    public async Task Ready_event_with_new_transport_id_is_idempotent_and_does_not_reset_stock()
    {
        using var host = CreateHost();
        await host.Services.InitializeWarehouseDatabaseAsync();
        await host.StartAsync();
        try
        {
            var shipment = Guid.NewGuid();
            var number = PersistenceTests.Number();
            var message = new ShipmentReadyForPickup(Guid.NewGuid(), shipment, DateTimeOffset.UtcNow, shipment, number);
            var bus = host.Services.GetRequiredService<IBus>();
            var firstId = Guid.NewGuid();
            await bus.Publish(message, context => context.MessageId = firstId);
            await WaitForInbox(firstId);
            var site = await CreateSite(host);
            await Send(host, new ScanParcel(Guid.NewGuid(), site.Id, number, ScanDirection.Entry, "operator"));
            var duplicateId = Guid.NewGuid();
            await bus.Publish(message, context => context.MessageId = duplicateId);
            await WaitForInbox(duplicateId);
            await using var database = CreateContext();
            var parcel = await database.Inventory.SingleAsync(x => x.ShipmentId == shipment);
            parcel.CurrentWarehouseId.Should().Be(site.Id);
            parcel.HasBeenPickedUp.Should().BeTrue();
            (await database.Scans.CountAsync(x => x.ShipmentId == shipment)).Should().Be(1);
        }
        finally { await host.StopAsync(); }
    }

    [Fact]
    public async Task Concurrent_entries_for_one_parcel_produce_one_pickup_and_one_stock_movement()
    {
        using var host = CreateHost();
        await host.Services.InitializeWarehouseDatabaseAsync();
        await host.StartAsync();
        var shipment = Guid.NewGuid();
        var number = PersistenceTests.Number();
        var site = await CreateSite(host);
        await Send(host, new RegisterReadyShipment(shipment, number));
        var observed = new ConcurrentBag<ParcelPickedUp>();
        var bus = host.Services.GetRequiredService<IBus>();
        var handle = bus.ConnectReceiveEndpoint($"warehouse-observer-{Guid.NewGuid():N}", endpoint =>
            endpoint.Handler<ParcelPickedUp>(context =>
            {
                if (context.Message.ShipmentId == shipment) observed.Add(context.Message);
                return Task.CompletedTask;
            }));
        await handle.Ready;
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
            {
                try { await Send(host, new ScanParcel(Guid.NewGuid(), site.Id, number, ScanDirection.Entry, "operator")); return true; }
                catch (WarehouseConflictException) { return false; }
            }));
            results.Count(x => x).Should().Be(1);
            await WaitUntil(() => observed.Count == 1);
            await using var database = CreateContext();
            (await database.Scans.CountAsync(x => x.ShipmentId == shipment)).Should().Be(1);
            (await database.Inventory.SingleAsync(x => x.ShipmentId == shipment)).CurrentWarehouseId.Should().Be(site.Id);
            observed.Should().ContainSingle();
        }
        finally { await handle.StopAsync(); await host.StopAsync(); }
    }

    [Fact]
    public async Task Scan_id_collision_rolls_back_inventory_movement_and_outbox_events()
    {
        using var host = CreateHost();
        await host.Services.InitializeWarehouseDatabaseAsync();
        var site = await CreateSite(host);
        var firstShipment = Guid.NewGuid(); var secondShipment = Guid.NewGuid();
        var firstNumber = PersistenceTests.Number(); var secondNumber = PersistenceTests.Number();
        await Send(host, new RegisterReadyShipment(firstShipment, firstNumber));
        await Send(host, new RegisterReadyShipment(secondShipment, secondNumber));
        var scanId = Guid.NewGuid();
        await Send(host, new ScanParcel(scanId, site.Id, firstNumber, ScanDirection.Entry, "operator"));
        var write = () => Send(host, new ScanParcel(scanId, site.Id, secondNumber, ScanDirection.Entry, "operator"));
        await FluentActions.Awaiting(write).Should().ThrowAsync<WarehouseConflictException>();
        await using var database = CreateContext();
        var second = await database.Inventory.SingleAsync(x => x.ShipmentId == secondShipment);
        second.CurrentWarehouseId.Should().BeNull();
        second.HasBeenPickedUp.Should().BeFalse();
        (await database.Scans.AnyAsync(x => x.ShipmentId == secondShipment)).Should().BeFalse();
        (await database.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().AnyAsync(x => x.Body.Contains(secondShipment.ToString()))).Should().BeFalse();
    }

    [Fact]
    public async Task Deactivation_racing_entry_cannot_leave_parcel_in_an_inactive_warehouse()
    {
        using var host = CreateHost();
        await host.Services.InitializeWarehouseDatabaseAsync();
        var site = await CreateSite(host);
        var shipment = Guid.NewGuid(); var number = PersistenceTests.Number();
        await Send(host, new RegisterReadyShipment(shipment, number));
        var entry = Task.Run(async () =>
        {
            try { await Send(host, new ScanParcel(Guid.NewGuid(), site.Id, number, ScanDirection.Entry, "operator")); return true; }
            catch (WarehouseConflictException) { return false; }
        });
        var deactivate = Task.Run(async () =>
        {
            try { await Send(host, new UpdateWarehouse(site.Id, site.Name, site.Street, site.City, site.PostalCode, site.CountryCode, false)); return true; }
            catch (WarehouseConflictException) { return false; }
        });
        var results = await Task.WhenAll(entry, deactivate);
        results.Count(x => x).Should().Be(1);
        await using var database = CreateContext();
        var persistedSite = await database.Warehouses.SingleAsync(x => x.Id == site.Id);
        var persistedParcel = await database.Inventory.SingleAsync(x => x.ShipmentId == shipment);
        (persistedSite.IsActive || persistedParcel.CurrentWarehouseId is null).Should().BeTrue();
    }

    [Fact]
    public async Task Stock_pagination_is_stable_and_excludes_departed_parcels()
    {
        using var host = CreateHost();
        await host.Services.InitializeWarehouseDatabaseAsync();
        var site = await CreateSite(host);
        var numbers = Enumerable.Range(0, 5).Select(_ => PersistenceTests.Number()).Order().ToArray();
        foreach (var number in numbers)
        {
            await Send(host, new RegisterReadyShipment(Guid.NewGuid(), number));
            await Send(host, new ScanParcel(Guid.NewGuid(), site.Id, number, ScanDirection.Entry, "operator"));
        }
        await Send(host, new ScanParcel(Guid.NewGuid(), site.Id, numbers[0], ScanDirection.Exit, "operator"));
        var first = await Send(host, new GetWarehouseStock(site.Id, 1, 2));
        var second = await Send(host, new GetWarehouseStock(site.Id, 2, 2));
        first.Total.Should().Be(4);
        first.Items.Select(x => x.TrackingNumber).Should().Equal(numbers.Skip(1).Take(2));
        second.Items.Select(x => x.TrackingNumber).Should().Equal(numbers.Skip(3));
    }

    [Fact]
    public async Task Database_failure_after_mutation_rolls_back_stock_scan_and_outbox_atomically()
    {
        using var host = CreateHost();
        await host.Services.InitializeWarehouseDatabaseAsync();
        var site = await CreateSite(host);
        var shipment = Guid.NewGuid(); var number = PersistenceTests.Number();
        await Send(host, new RegisterReadyShipment(shipment, number));
        await using var database = CreateContext();
        await database.Database.ExecuteSqlRawAsync("ALTER TABLE warehouse_scans ADD CONSTRAINT test_rollback_guard CHECK (\"OperatorSubject\" <> 'rollback-test')");
        try
        {
            var write = () => Send(host, new ScanParcel(Guid.NewGuid(), site.Id, number, ScanDirection.Entry, "rollback-test"));
            await FluentActions.Awaiting(write).Should().ThrowAsync<DbUpdateException>();
            await using var verification = CreateContext();
            var parcel = await verification.Inventory.AsNoTracking().SingleAsync(x => x.ShipmentId == shipment);
            parcel.CurrentWarehouseId.Should().BeNull();
            parcel.HasBeenPickedUp.Should().BeFalse();
            parcel.LastScanAtUtc.Should().BeNull();
            (await verification.Scans.AnyAsync(x => x.ShipmentId == shipment)).Should().BeFalse();
            (await verification.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().AnyAsync(x => x.Body.Contains(shipment.ToString()))).Should().BeFalse();
        }
        finally
        {
            await database.Database.ExecuteSqlRawAsync("ALTER TABLE warehouse_scans DROP CONSTRAINT test_rollback_guard");
        }
    }
    private async Task WaitForInbox(Guid messageId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            await using var database = CreateContext();
            if (await database.Set<MassTransit.EntityFrameworkCoreIntegration.InboxState>().AnyAsync(x => x.MessageId == messageId && x.Delivered != null, timeout.Token)) return;
            await Task.Delay(100, timeout.Token);
        }
    }
    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!condition()) await Task.Delay(100, timeout.Token);
    }
}
