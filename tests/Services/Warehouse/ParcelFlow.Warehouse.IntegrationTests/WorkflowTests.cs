using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.Shipment.Domain;
using ParcelFlow.Shipment.Infrastructure;
using ParcelFlow.Shipment.Infrastructure.Persistence;
using ParcelFlow.Tracking.Application;
using ParcelFlow.Tracking.Domain;
using ParcelFlow.Tracking.Infrastructure;
using ParcelFlow.Tracking.Infrastructure.Persistence;
using ParcelFlow.Warehouse.Application;
using ParcelFlow.Warehouse.Domain;
using ParcelFlow.Warehouse.Infrastructure;
using ParcelFlow.Warehouse.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Warehouse.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class WorkflowTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Paid_shipment_scans_complete_booking_and_update_tracking_across_three_databases()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var shipmentConnection = DatabaseConnection($"workflow_shipment_{suffix}");
        var trackingConnection = DatabaseConnection($"workflow_tracking_{suffix}");
        var warehouseConnection = DatabaseConnection($"workflow_warehouse_{suffix}");
        using var shipmentHost = CreateHost("ShipmentDatabase", shipmentConnection,
            (services, configuration) => services.AddShipmentInfrastructure(configuration));
        using var trackingHost = CreateHost("TrackingDatabase", trackingConnection,
            (services, configuration) => services.AddTrackingApplication().AddTrackingInfrastructure(configuration));
        using var warehouseHost = CreateHost("WarehouseDatabase", warehouseConnection,
            (services, configuration) => services.AddWarehouseApplication().AddWarehouseInfrastructure(configuration));
        await shipmentHost.Services.InitializeShipmentDatabaseAsync();
        await trackingHost.Services.InitializeTrackingDatabaseAsync();
        await warehouseHost.Services.InitializeWarehouseDatabaseAsync();
        await shipmentHost.StartAsync();
        await trackingHost.StartAsync();
        await warehouseHost.StartAsync();
        try
        {
            var number = "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
            var shipment = new ParcelShipment("workflow-customer", number,
                new ShippingAddress("Emma Martin", "emma@example.test", "+33600000001", "12 rue des Peupliers", null, "69003", "Lyon", "FR"),
                new ShippingAddress("Lucas Bernard", "lucas@example.test", "+33600000002", "28 rue des Tilleuls", null, "75012", "Paris", "FR"),
                new ParcelSpecification(2, 20, 15, 10), 7.30m, DeliveryZone.Domestic);
            await using (var scope = shipmentHost.Services.CreateAsyncScope())
            {
                var database = scope.ServiceProvider.GetRequiredService<ShipmentDbContext>();
                database.Shipments.Add(shipment);
                await database.SaveChangesAsync();
            }
            var bus = shipmentHost.Services.GetRequiredService<IBus>();
            await bus.Publish(new ShipmentCreated(Guid.NewGuid(), shipment.Id, DateTimeOffset.UtcNow,
                shipment.Id, number, shipment.OwnerSubject, shipment.Price, shipment.Currency));
            await WaitUntil(async () =>
            {
                await using var scope = shipmentHost.Services.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<ShipmentDbContext>().Bookings
                    .AnyAsync(x => x.CorrelationId == shipment.Id && x.CurrentState == "AwaitingPayment");
            });
            await bus.Publish(new PaymentCompleted(Guid.NewGuid(), shipment.Id, DateTimeOffset.UtcNow,
                shipment.Id, shipment.Price, shipment.Currency));
            await WaitUntil(async () =>
            {
                await using var scope = warehouseHost.Services.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<IWarehouseStore>()
                    .FindInventoryAsync(number, CancellationToken.None) is not null;
            });
            var departure = await Send(warehouseHost, new CreateWarehouse(Guid.NewGuid(), "FR-WORKFLOW-01", "Dépôt de départ",
                WarehouseKind.Warehouse, "12 rue des Peupliers", "Lyon", "69003", "FR"));
            var sorting = await Send(warehouseHost, new CreateWarehouse(Guid.NewGuid(), "FR-WORKFLOW-02", "Centre de tri",
                WarehouseKind.SortingCenter, "28 rue des Tilleuls", "Paris", "75012", "FR"));
            var entry = new ScanParcel(Guid.NewGuid(), departure.Id, number, ScanDirection.Entry, "workflow-operator");
            var receipt = await Send(warehouseHost, entry);
            await WaitUntil(async () =>
            {
                await using var scope = shipmentHost.Services.CreateAsyncScope();
                var database = scope.ServiceProvider.GetRequiredService<ShipmentDbContext>();
                return await database.Bookings.AnyAsync(x => x.CorrelationId == shipment.Id && x.CurrentState == "Completed") &&
                    await database.Shipments.AnyAsync(x => x.Id == shipment.Id && x.Status == ShipmentStatus.PickedUp);
            });
            await Send(warehouseHost, new ScanParcel(Guid.NewGuid(), departure.Id, number, ScanDirection.Exit, "workflow-operator"));
            await Send(warehouseHost, new ScanParcel(Guid.NewGuid(), sorting.Id, number, ScanDirection.Entry, "workflow-operator"));
            // A delayed HTTP retry must return its original receipt without undoing later movements.
            (await Send(warehouseHost, entry)).Should().Be(receipt);
            (await Send(warehouseHost, new GetWarehouseStock(departure.Id))).Total.Should().Be(0);
            var stock = await Send(warehouseHost, new GetWarehouseStock(sorting.Id));
            stock.Items.Should().ContainSingle(x => x.ShipmentId == shipment.Id && x.TrackingNumber == number);
            await WaitUntil(async () =>
            {
                await using var scope = trackingHost.Services.CreateAsyncScope();
                var record = await scope.ServiceProvider.GetRequiredService<ITrackingStore>()
                    .FindByTrackingNumberAsync(number, CancellationToken.None);
                return record?.Entries.Count == 4 && record.CurrentStatus == ParcelStatus.AtSortingCenter;
            });
            var tracking = await Send(trackingHost, new GetPublicTracking(number));
            tracking.History.Select(x => x.Status).Should().Equal(
                ParcelStatus.Created, ParcelStatus.PickedUp, ParcelStatus.InTransit, ParcelStatus.AtSortingCenter);
            tracking.History.Last().FacilityCode.Should().Be(sorting.Code);
        }
        finally
        {
            await warehouseHost.StopAsync();
            await trackingHost.StopAsync();
            await shipmentHost.StopAsync();
        }
    }

    private string DatabaseConnection(string database) => new NpgsqlConnectionStringBuilder(infrastructure.Postgres.GetConnectionString())
    { Database = database }.ConnectionString;

    private IHost CreateHost(string connectionName, string connection, Action<IServiceCollection, IConfiguration> register) =>
        Host.CreateDefaultBuilder().ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{connectionName}"] = connection,
            ["ConnectionStrings:Redis"] = infrastructure.Redis.GetConnectionString(),
            ["RabbitMq:Host"] = infrastructure.RabbitMq.Hostname,
            ["RabbitMq:Port"] = infrastructure.RabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:Username"] = "rabbitmq", ["RabbitMq:Password"] = "rabbitmq"
        })).ConfigureServices((context, services) =>
        {
            register(services, context.Configuration);
            services.Configure<MassTransitHostOptions>(options => options.WaitUntilStarted = true);
        }).Build();

    private static async Task<T> Send<T>(IHost host, IRequest<T> request)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private static async Task WaitUntil(Func<Task<bool>> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        while (!await predicate()) await Task.Delay(100, deadline.Token);
    }
}
