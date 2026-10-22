using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Warehouse.Application;
using ParcelFlow.Warehouse.Domain;
using ParcelFlow.Warehouse.Infrastructure;
using ParcelFlow.Warehouse.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Warehouse.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class DeliveryExitConsumerTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Out_for_delivery_event_exits_parcel_in_stock_automatically()
    {
        using var host = CreateHost();
        await host.Services.InitializeWarehouseDatabaseAsync();
        await host.StartAsync();
        try
        {
            var (shipment, number, site) = await StockParcel(host);
            var message = Status(shipment, number, TrackingStatus.OutForDelivery);
            var messageId = Guid.NewGuid();
            await host.Services.GetRequiredService<IBus>().Publish(message, context => context.MessageId = messageId);
            await WaitForInbox(messageId);
            await using var database = CreateContext();
            (await database.Inventory.SingleAsync(x => x.ShipmentId == shipment)).CurrentWarehouseId.Should().BeNull();
            var exit = await database.Scans.SingleAsync(x => x.ShipmentId == shipment && x.Direction == ScanDirection.Exit);
            exit.ScanId.Should().Be(WarehouseRules.DeliveryExitScanId(message.EventId));
            exit.WarehouseId.Should().Be(site.Id);
            exit.OperatorSubject.Should().Be(WarehouseRules.DeliveryStartOperator);
            (await Send(host, new GetWarehouseStock(site.Id))).Items.Should().NotContain(x => x.ShipmentId == shipment);
        }
        finally { await host.StopAsync(); }
    }

    [Fact]
    public async Task Delivery_event_for_parcel_already_exited_is_a_no_op()
    {
        using var host = CreateHost();
        await host.Services.InitializeWarehouseDatabaseAsync();
        await host.StartAsync();
        try
        {
            var (shipment, number, site) = await StockParcel(host);
            await Send(host, new ScanParcel(Guid.NewGuid(), site.Id, number, ScanDirection.Exit, "operator"));
            var messageId = Guid.NewGuid();
            await host.Services.GetRequiredService<IBus>().Publish(Status(shipment, number, TrackingStatus.OutForDelivery), context => context.MessageId = messageId);
            await WaitForInbox(messageId);
            await using var database = CreateContext();
            (await database.Scans.CountAsync(x => x.ShipmentId == shipment)).Should().Be(2);
            (await database.Scans.CountAsync(x => x.ShipmentId == shipment && x.OperatorSubject == WarehouseRules.DeliveryStartOperator)).Should().Be(0);
        }
        finally { await host.StopAsync(); }
    }

    [Fact]
    public async Task Redelivered_event_and_later_delivered_status_produce_a_single_exit()
    {
        using var host = CreateHost();
        await host.Services.InitializeWarehouseDatabaseAsync();
        await host.StartAsync();
        try
        {
            var (shipment, number, _) = await StockParcel(host);
            var bus = host.Services.GetRequiredService<IBus>();
            var message = Status(shipment, number, TrackingStatus.OutForDelivery);
            // New transport IDs bypass the inbox, so idempotency must come from the derived scan ID.
            var messageIds = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
            await Task.WhenAll(messageIds.Select(id => bus.Publish(message, context => context.MessageId = id)));
            var deliveredId = Guid.NewGuid();
            await bus.Publish(Status(shipment, number, TrackingStatus.Delivered) with { OccurredAtUtc = message.OccurredAtUtc.AddMinutes(20) },
                context => context.MessageId = deliveredId);
            foreach (var id in messageIds.Append(deliveredId)) await WaitForInbox(id);
            await using var database = CreateContext();
            (await database.Scans.CountAsync(x => x.ShipmentId == shipment && x.Direction == ScanDirection.Exit)).Should().Be(1);
            (await database.Inventory.SingleAsync(x => x.ShipmentId == shipment)).CurrentWarehouseId.Should().BeNull();
        }
        finally { await host.StopAsync(); }
    }

    private static ParcelStatusRecorded Status(Guid shipment, string number, TrackingStatus status) =>
        new(Guid.NewGuid(), shipment, DateTimeOffset.UtcNow.AddMinutes(1), shipment, number, status, null);

    private static async Task<(Guid Shipment, string Number, WarehouseDto Site)> StockParcel(IHost host)
    {
        var shipment = Guid.NewGuid();
        var number = PersistenceTests.Number();
        var site = await Send(host, new CreateWarehouse(Guid.NewGuid(), "FR-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Site de test",
            WarehouseKind.Warehouse, "18 avenue Tony Garnier", "Lyon", "69007", "FR"));
        await Send(host, new RegisterReadyShipment(shipment, number));
        await Send(host, new ScanParcel(Guid.NewGuid(), site.Id, number, ScanDirection.Entry, "operator"));
        return (shipment, number, site);
    }

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
}
