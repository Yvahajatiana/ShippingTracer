using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.Delivery.Application;
using ParcelFlow.Delivery.Domain;
using ParcelFlow.Delivery.Infrastructure;
using ParcelFlow.Delivery.Infrastructure.Persistence;
using ParcelFlow.Tracking.Application;
using ParcelFlow.Tracking.Domain;
using ParcelFlow.Tracking.Infrastructure;
using ParcelFlow.Tracking.Infrastructure.Persistence;
using ParcelFlow.Warehouse.Infrastructure;
using ParcelFlow.Warehouse.Application;
using ParcelFlow.Warehouse.Infrastructure.Persistence;
using Xunit;
using D = ParcelFlow.Delivery.Application;
using W = ParcelFlow.Warehouse.Application;

namespace ParcelFlow.Delivery.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class WorkflowTests(InfrastructureFixture infrastructure)
{
    private sealed class WorkflowActor : D.IDeliveryActor
    {
        public string Subject => "workflow-driver";
        public bool IsAdmin => false;
    }

    [Fact]
    public async Task Warehouse_pickup_failed_delivery_and_replanned_proof_reach_tracking_without_losing_stock_history()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var warehouse = CreateHost("WarehouseDatabase", Connection($"delivery_workflow_warehouse_{suffix}"),
            (services, settings) => services.AddWarehouseApplication().AddWarehouseInfrastructure(settings));
        using var tracking = CreateHost("TrackingDatabase", Connection($"delivery_workflow_tracking_{suffix}"),
            (services, settings) => services.AddTrackingApplication().AddTrackingInfrastructure(settings));
        using var delivery = CreateHost("DeliveryDatabase", Connection($"delivery_workflow_delivery_{suffix}"),
            (services, settings) =>
            {
                services.AddDeliveryApplication().AddDeliveryInfrastructure(settings);
                services.AddScoped<D.IDeliveryActor, WorkflowActor>();
            });
        await warehouse.Services.InitializeWarehouseDatabaseAsync();
        await tracking.Services.InitializeTrackingDatabaseAsync();
        await delivery.Services.InitializeDeliveryDatabaseAsync();
        await warehouse.StartAsync();
        await tracking.StartAsync();
        await delivery.StartAsync();
        try
        {
            var shipmentId = Guid.NewGuid();
            var number = "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
            var bus = warehouse.Services.GetRequiredService<IBus>();
            var created = new ShipmentCreated(Guid.NewGuid(), shipmentId, DateTimeOffset.UtcNow,
                shipmentId, number, "workflow-customer", 7.30m, "EUR");
            await bus.Publish(created);
            await bus.Publish(new ShipmentReadyForPickup(Guid.NewGuid(), shipmentId, DateTimeOffset.UtcNow, shipmentId, number));
            await WaitUntil(async () =>
            {
                await using var scope = warehouse.Services.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<W.IWarehouseStore>()
                    .FindInventoryAsync(number, CancellationToken.None) is not null;
            });
            var site = await Send(warehouse, new W.CreateWarehouse(Guid.NewGuid(), "FR-DELIVERY-01", "Dépôt de livraison",
                ParcelFlow.Warehouse.Domain.WarehouseKind.Warehouse, "18 avenue Tony Garnier", "Lyon", "69007", "FR"));
            await Send(warehouse, new W.ScanParcel(Guid.NewGuid(), site.Id, number,
                ParcelFlow.Warehouse.Domain.ScanDirection.Entry, "workflow-warehouse-operator"));
            await Send(warehouse, new W.ScanParcel(Guid.NewGuid(), site.Id, number,
                ParcelFlow.Warehouse.Domain.ScanDirection.Exit, "workflow-warehouse-operator"));
            await WaitUntil(async () =>
            {
                await using var scope = delivery.Services.CreateAsyncScope();
                var parcel = await scope.ServiceProvider.GetRequiredService<D.IDeliveryStore>()
                    .FindParcelAsync(shipmentId, CancellationToken.None);
                return parcel is { IsPickedUp: true } && parcel.TrackingNumber == number;
            });
            (await Send(warehouse, new W.GetWarehouseStock(site.Id))).Total.Should().Be(0);
            await Send(delivery, new D.CreateDriver("workflow-driver", "Camille Martin"));
            var first = await Plan(delivery, shipmentId, "Première tentative");
            await Send(delivery, new D.StartRoute(first.Id));
            var failureCommand = new D.FailDelivery(Guid.NewGuid(), first.Id, shipmentId, DeliveryFailureReason.RecipientAbsent);
            var failed = await Send(delivery, failureCommand);
            (await Send(delivery, new D.GetRoute(first.Id))).Status.Should().Be(DeliveryRouteStatus.Completed);
            (await Send(delivery, new D.ListAvailableParcels())).Items.Should().ContainSingle(x => x.ShipmentId == shipmentId);

            var retry = await Plan(delivery, shipmentId, "Nouvelle présentation");
            await Send(delivery, new D.StartRoute(retry.Id));
            var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGNgAAIAAAUAAXpeqz8AAAAASUVORK5CYII=");
            var successCommand = new D.ConfirmDelivery(Guid.NewGuid(), retry.Id, shipmentId, "Lucas Bernard", image, image);
            var receipt = await Send(delivery, successCommand);
            (await Send(delivery, successCommand)).Should().Be(receipt);
            // An old retry must keep its original failed receipt, without undoing the delivered state.
            (await Send(delivery, failureCommand)).Should().Be(failed);
            (await Send(delivery, new D.GetRoute(retry.Id))).Status.Should().Be(DeliveryRouteStatus.Completed);
            (await Send(delivery, new D.ListAvailableParcels())).Items.Should().NotContain(x => x.ShipmentId == shipmentId);
            var proof = await Send(delivery, new D.GetDeliveryProof(retry.Id, shipmentId));
            proof.HasSignature.Should().BeTrue(); proof.HasPhoto.Should().BeTrue();
            var storedImage = await Send(delivery, new D.GetDeliveryProofImage(retry.Id, shipmentId, D.ProofImageKind.Photo));
            storedImage.Content.Should().Equal(image); storedImage.ContentType.Should().Be("image/png");
            await WaitUntil(async () =>
            {
                await using var scope = tracking.Services.CreateAsyncScope();
                var record = await scope.ServiceProvider.GetRequiredService<ITrackingStore>()
                    .FindByTrackingNumberAsync(number, CancellationToken.None);
                return record?.Entries.Count == 7 && record.CurrentStatus == ParcelStatus.Delivered;
            });
            var publicTracking = await Send(tracking, new GetPublicTracking(number));
            publicTracking.History.Select(x => x.Status).Should().Equal(ParcelStatus.Created, ParcelStatus.PickedUp,
                ParcelStatus.InTransit, ParcelStatus.OutForDelivery, ParcelStatus.Failed, ParcelStatus.OutForDelivery, ParcelStatus.Delivered);
            publicTracking.CurrentStatusLabel.Should().Be("Livré");
        }
        finally
        {
            await delivery.StopAsync();
            await tracking.StopAsync();
            await warehouse.StopAsync();
        }
    }

    private static async Task<D.RouteDto> Plan(IHost host, Guid shipmentId, string name)
    {
        var route = await Send(host, new D.CreateRoute(Guid.NewGuid(), name, new DateOnly(2026, 10, 18)));
        await Send(host, new D.AssignRouteDriver(route.Id, "workflow-driver"));
        return await Send(host, new D.AddRouteParcel(route.Id, shipmentId));
    }
    private string Connection(string database) => new NpgsqlConnectionStringBuilder(infrastructure.Postgres.GetConnectionString())
        { Database = database }.ConnectionString;
    private IHost CreateHost(string key, string connection, Action<IServiceCollection, IConfiguration> register) =>
        Host.CreateDefaultBuilder().ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{key}"] = connection,
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
