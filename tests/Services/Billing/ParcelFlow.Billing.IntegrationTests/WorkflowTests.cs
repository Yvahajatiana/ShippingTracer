using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using ParcelFlow.Billing.Application;
using ParcelFlow.Billing.Domain;
using ParcelFlow.Billing.Infrastructure;
using ParcelFlow.Billing.Infrastructure.Persistence;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.Shipment.Domain;
using ParcelFlow.Shipment.Infrastructure;
using ParcelFlow.Shipment.Infrastructure.Persistence;
using ParcelFlow.Tracking.Application;
using ParcelFlow.Tracking.Domain;
using ParcelFlow.Tracking.Infrastructure;
using ParcelFlow.Tracking.Infrastructure.Persistence;
using ParcelFlow.Warehouse.Application;
using ParcelFlow.Warehouse.Infrastructure;
using ParcelFlow.Warehouse.Infrastructure.Persistence;
using Xunit;
using B = ParcelFlow.Billing.Application;
using W = ParcelFlow.Warehouse.Application;

namespace ParcelFlow.Billing.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class WorkflowTests(InfrastructureFixture infrastructure)
{
    private sealed class WorkflowAdmin : IBillingActor
    {
        public string Subject => "workflow-admin";
        public bool IsAdmin => true;
    }

    [Fact]
    public async Task Real_booking_saga_waits_for_manual_payment_handles_decline_and_keeps_pickup_after_credits()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var shipment = CreateHost("ShipmentDatabase", Connection($"billing_workflow_shipment_{suffix}"),
            (services, settings) => services.AddShipmentInfrastructure(settings));
        using var billing = CreateHost("BillingDatabase", Connection($"billing_workflow_billing_{suffix}"),
            (services, settings) =>
            {
                services.AddBillingApplication().AddBillingInfrastructure(settings);
                services.AddScoped<IBillingActor, WorkflowAdmin>();
            });
        using var warehouse = CreateHost("WarehouseDatabase", Connection($"billing_workflow_warehouse_{suffix}"),
            (services, settings) => services.AddWarehouseApplication().AddWarehouseInfrastructure(settings));
        using var tracking = CreateHost("TrackingDatabase", Connection($"billing_workflow_tracking_{suffix}"),
            (services, settings) => services.AddTrackingApplication().AddTrackingInfrastructure(settings));
        await shipment.Services.InitializeShipmentDatabaseAsync();
        await billing.Services.InitializeBillingDatabaseAsync();
        await warehouse.Services.InitializeWarehouseDatabaseAsync();
        await tracking.Services.InitializeTrackingDatabaseAsync();
        await shipment.StartAsync();
        await warehouse.StartAsync();
        await tracking.StartAsync();
        try
        {
            var accepted = await CreateShipment(shipment);
            await WaitForBooking(shipment, accepted.Id, "AwaitingPayment", ShipmentStatus.AwaitingPayment);
            // Billing starts after the saga sent its command to the durable billing-payments queue.
            await billing.StartAsync();
            await WaitForInvoice(billing, accepted.Id);
            var invoice = await Send(billing, new B.GetInvoice(accepted.Id));
            invoice.Status.Should().Be(InvoiceStatus.PendingPayment);
            invoice.Amount.Should().Be(accepted.Price);
            await WaitForBooking(shipment, accepted.Id, "AwaitingPayment", ShipmentStatus.AwaitingPayment);
            (await WarehouseParcel(warehouse, accepted.TrackingNumber)).Should().BeNull();

            var pay = new B.PayInvoice(invoice.Id, Guid.NewGuid(), MockPaymentOutcome.Approved);
            var receipt = await Send(billing, pay);
            await WaitForBooking(shipment, accepted.Id, "AwaitingPickup", ShipmentStatus.ReadyForPickup);
            await WaitUntil(async () => await WarehouseParcel(warehouse, accepted.TrackingNumber) is not null);
            var site = await Send(warehouse, new W.CreateWarehouse(Guid.NewGuid(), "FR-BILLING-01", "Dépôt de prise en charge",
                ParcelFlow.Warehouse.Domain.WarehouseKind.Warehouse, "18 avenue Tony Garnier", "Lyon", "69007", "FR"));
            await Send(warehouse, new W.ScanParcel(Guid.NewGuid(), site.Id, accepted.TrackingNumber,
                ParcelFlow.Warehouse.Domain.ScanDirection.Entry, "workflow-warehouse-operator"));
            await WaitForBooking(shipment, accepted.Id, "Completed", ShipmentStatus.PickedUp);

            var creditCommand = new B.IssueCreditNote(invoice.Id, Guid.NewGuid(), 2m, CreditReason.ServiceIssue);
            var firstCredit = await Send(billing, creditCommand);
            (await Send(billing, creditCommand)).Should().Be(firstCredit);
            (await Send(billing, new B.GetInvoice(invoice.Id))).Status.Should().Be(InvoiceStatus.PartiallyCredited);
            await Send(billing, new B.IssueCreditNote(invoice.Id, Guid.NewGuid(), accepted.Price - 2m, CreditReason.BillingCorrection));
            var credited = await Send(billing, new B.GetInvoice(invoice.Id));
            credited.Status.Should().Be(InvoiceStatus.Credited);
            credited.CreditTotal.Should().Be(accepted.Price);
            credited.RemainingCredit.Should().Be(0);
            (await Send(billing, pay)).Should().Be(receipt);
            (await Send(billing, new B.ListInvoiceCreditNotes(invoice.Id))).Total.Should().Be(2);
            await WaitForBooking(shipment, accepted.Id, "Completed", ShipmentStatus.PickedUp);

            var declined = await CreateShipment(shipment);
            await WaitForInvoice(billing, declined.Id);
            await WaitForBooking(shipment, declined.Id, "AwaitingPayment", ShipmentStatus.AwaitingPayment);
            var reject = new B.PayInvoice(declined.Id, Guid.NewGuid(), MockPaymentOutcome.Declined);
            var rejectedReceipt = await Send(billing, reject);
            await WaitForBooking(shipment, declined.Id, "PaymentRejected", ShipmentStatus.PaymentFailed);
            (await Send(billing, reject)).Should().Be(rejectedReceipt);
            (await Send(billing, new B.GetInvoice(declined.Id))).Status.Should().Be(InvoiceStatus.PaymentFailed);
            var forbiddenRetry = () => Send(billing, new B.PayInvoice(declined.Id, Guid.NewGuid(), MockPaymentOutcome.Approved));
            await FluentActions.Awaiting(forbiddenRetry).Should().ThrowAsync<BillingConflictException>();
            (await WarehouseParcel(warehouse, declined.TrackingNumber)).Should().BeNull();

            await WaitUntil(async () =>
            {
                await using var scope = tracking.Services.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<ITrackingStore>();
                var pickedUp = await store.FindByTrackingNumberAsync(accepted.TrackingNumber, CancellationToken.None);
                var unpaid = await store.FindByTrackingNumberAsync(declined.TrackingNumber, CancellationToken.None);
                return pickedUp?.CurrentStatus == ParcelStatus.PickedUp && unpaid?.CurrentStatus == ParcelStatus.Created;
            });
        }
        finally
        {
            await billing.StopAsync();
            await tracking.StopAsync();
            await warehouse.StopAsync();
            await shipment.StopAsync();
        }
    }

    private static async Task<ParcelShipment> CreateShipment(IHost host)
    {
        var value = new ParcelShipment("workflow-customer", "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant(),
            new ShippingAddress("Emma Martin", "emma@example.test", "+33600000001", "12 rue des Peupliers", null, "69003", "Lyon", "FR"),
            new ShippingAddress("Lucas Bernard", "lucas@example.test", "+33600000002", "28 rue des Tilleuls", null, "75012", "Paris", "FR"),
            new ParcelSpecification(2, 20, 15, 10), 7.30m, DeliveryZone.Domestic);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ShipmentDbContext>();
            database.Shipments.Add(value);
            await database.SaveChangesAsync();
        }
        await host.Services.GetRequiredService<IBus>().Publish(new ShipmentCreated(Guid.NewGuid(), value.Id,
            DateTimeOffset.UtcNow, value.Id, value.TrackingNumber, value.OwnerSubject, value.Price, value.Currency));
        return value;
    }
    private static async Task<ParcelFlow.Warehouse.Domain.ParcelInventory?> WarehouseParcel(IHost host, string number)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWarehouseStore>().FindInventoryAsync(number, CancellationToken.None);
    }
    private static Task WaitForInvoice(IHost host, Guid id) => WaitUntil(async () =>
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBillingStore>().FindInvoiceAsync(id, CancellationToken.None) is not null;
    });
    private static Task WaitForBooking(IHost host, Guid id, string state, ShipmentStatus status) => WaitUntil(async () =>
    {
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ShipmentDbContext>();
        return await database.Bookings.AnyAsync(x => x.CorrelationId == id && x.CurrentState == state) &&
            await database.Shipments.AnyAsync(x => x.Id == id && x.Status == status);
    });
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
