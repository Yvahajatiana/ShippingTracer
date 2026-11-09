using System.Collections.Concurrent;
using FluentAssertions;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ParcelFlow.Billing.Application;
using ParcelFlow.Billing.Domain;
using ParcelFlow.Billing.Infrastructure;
using ParcelFlow.Billing.Infrastructure.Persistence;
using ParcelFlow.BuildingBlocks.Application.Identity;
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using Xunit;

namespace ParcelFlow.Billing.IntegrationTests;

/// <summary>
/// Issue #39: a Shipment payment timeout must never let the customer be charged for the abandoned shipment,
/// whatever the arrival order of PaymentRequested, the timeout and the customer payment.
/// </summary>
[Collection(InfrastructureCollection.Name)]
public sealed class PaymentTimeoutTests(InfrastructureFixture infrastructure)
{
    private const string Customer = "billing-timeout-customer";
    private sealed record Actor(string Subject, bool IsAdmin) : IBillingActor;

    [Fact]
    public async Task Timeout_before_payment_request_never_opens_a_payable_invoice()
    {
        using var host = await StartHost();
        try
        {
            var shipment = Guid.NewGuid();
            var timeout = await PublishTimeout(host, shipment);
            await WaitForInbox(timeout);
            var request = await SendPaymentRequested(host, shipment, 12.90m);
            await WaitForInbox(request);
            // A redelivered timeout with a new EventId bypasses the Inbox and must still be a no-op.
            await WaitForInbox(await PublishTimeout(host, shipment));

            await using var database = CreateContext();
            (await database.Invoices.AnyAsync(x => x.Id == shipment)).Should().BeFalse();
            (await database.Payments.AnyAsync(x => x.InvoiceId == shipment)).Should().BeFalse();
            var record = await database.PaymentTimeouts.SingleAsync(x => x.ShipmentId == shipment);
            record.Resolution.Should().Be(PaymentTimeoutResolution.NoInvoice);
            await FluentActions.Awaiting(() => Send(host, new PayInvoice(shipment, Guid.NewGuid(), MockPaymentOutcome.Approved)))
                .Should().ThrowAsync<BillingNotFoundException>();
        }
        finally { await host.StopAsync(); }
    }

    [Fact]
    public async Task Timeout_before_capture_cancels_the_pending_invoice_and_rejects_the_payment()
    {
        using var host = await StartHost();
        try
        {
            var shipment = Guid.NewGuid();
            await WaitForInbox(await SendPaymentRequested(host, shipment, 12.90m));
            await WaitForInbox(await PublishTimeout(host, shipment));

            await FluentActions.Awaiting(() => Send(host, new PayInvoice(shipment, Guid.NewGuid(), MockPaymentOutcome.Approved)))
                .Should().ThrowAsync<BillingConflictException>().WithMessage("*payment window expired*");
            // A replayed PaymentRequested keeps the invoice cancelled.
            await WaitForInbox(await SendPaymentRequested(host, shipment, 12.90m));

            await using var database = CreateContext();
            var invoice = await database.Invoices.SingleAsync(x => x.Id == shipment);
            invoice.Status.Should().Be(InvoiceStatus.Cancelled); invoice.CreditTotal.Should().Be(0m);
            (await database.Payments.AnyAsync(x => x.InvoiceId == shipment)).Should().BeFalse();
            (await database.CreditNotes.AnyAsync(x => x.InvoiceId == shipment)).Should().BeFalse();
            (await database.PaymentTimeouts.SingleAsync(x => x.ShipmentId == shipment)).Resolution.Should().Be(PaymentTimeoutResolution.InvoiceCancelled);
            (await Send(host, new GetInvoice(shipment))).Status.Should().Be(InvoiceStatus.Cancelled);
        }
        finally { await host.StopAsync(); }
    }

    [Fact]
    public async Task Timeout_after_capture_refunds_the_customer_once_with_a_credit_note()
    {
        using var host = await StartHost();
        var bus = host.Services.GetRequiredService<IBus>();
        var credits = new ConcurrentQueue<CreditNoteIssued>();
        var shipment = Guid.NewGuid();
        var observer = bus.ConnectReceiveEndpoint("billing-timeout-credit-" + Guid.NewGuid().ToString("N"), endpoint =>
            endpoint.Handler<CreditNoteIssued>(context =>
            {
                if (context.Message.ShipmentId == shipment) credits.Enqueue(context.Message);
                return Task.CompletedTask;
            }));
        await observer.Ready;
        try
        {
            await WaitForInbox(await SendPaymentRequested(host, shipment, 12.90m));
            await Send(host, new PayInvoice(shipment, Guid.NewGuid(), MockPaymentOutcome.Approved));
            await Send(host, new IssueCreditNote(shipment, Guid.NewGuid(), 2.90m, CreditReason.ServiceIssue));
            await WaitForInbox(await PublishTimeout(host, shipment));
            await WaitForInbox(await PublishTimeout(host, shipment));
            await WaitForInbox(await SendPaymentRequested(host, shipment, 12.90m));

            await using var database = CreateContext();
            var invoice = await database.Invoices.SingleAsync(x => x.Id == shipment);
            invoice.Status.Should().Be(InvoiceStatus.Credited); invoice.CreditTotal.Should().Be(12.90m);
            (await database.Payments.CountAsync(x => x.InvoiceId == shipment)).Should().Be(1);
            var refund = await database.CreditNotes.SingleAsync(x => x.InvoiceId == shipment && x.Reason == CreditReason.PaymentTimeout);
            refund.Amount.Should().Be(10m); refund.ActorSubject.Should().Be(SettleShipmentPaymentTimeoutHandler.SystemActor);
            refund.Id.Should().Be(DeterministicGuid.Create("billing:payment-timeout-credit-note:", shipment));
            (await database.CreditNotes.CountAsync(x => x.InvoiceId == shipment)).Should().Be(2);
            (await database.PaymentTimeouts.SingleAsync(x => x.ShipmentId == shipment)).Resolution.Should().Be(PaymentTimeoutResolution.Refunded);

            // Notification informs the customer through the existing CreditNoteIssued fact: one per credit note, never twice.
            await WaitUntil(() => credits.Count(x => x.CreditNoteId == refund.Id) == 1);
            await Task.Delay(TimeSpan.FromSeconds(2));
            var fact = credits.Should().ContainSingle(x => x.CreditNoteId == refund.Id).Subject;
            fact.EventId.Should().Be(DeterministicGuid.Create("billing:credit-note-issued:", refund.Id));
            fact.CorrelationId.Should().Be(shipment); fact.CustomerSubject.Should().Be(Customer); fact.Amount.Should().Be(10m); fact.Currency.Should().Be("EUR");
        }
        finally { await observer.StopAsync(); await host.StopAsync(); }
    }

    [Fact]
    public async Task Concurrent_payment_and_timeout_never_leave_a_captured_payment_unrefunded()
    {
        using var host = CreateHost(); await host.Services.InitializeBillingDatabaseAsync();
        var shipments = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var shipment in shipments) await Send(host, new RegisterInvoice(shipment, Customer, 9.99m, "EUR", DateTimeOffset.UtcNow));
        await Task.WhenAll(shipments.SelectMany(shipment => new[]
        {
            Attempt(() => Send(host, new PayInvoice(shipment, Guid.NewGuid(), MockPaymentOutcome.Approved))),
            Attempt(() => Send(host, new SettleShipmentPaymentTimeout(shipment, DateTimeOffset.UtcNow)))
        }));

        await using var database = CreateContext();
        foreach (var shipment in shipments)
        {
            var invoice = await database.Invoices.SingleAsync(x => x.Id == shipment);
            var paid = await database.Payments.AnyAsync(x => x.InvoiceId == shipment);
            var timeout = await database.PaymentTimeouts.SingleAsync(x => x.ShipmentId == shipment);
            if (paid)
            {
                invoice.Status.Should().Be(InvoiceStatus.Credited); invoice.CreditTotal.Should().Be(9.99m);
                timeout.Resolution.Should().Be(PaymentTimeoutResolution.Refunded);
            }
            else
            {
                invoice.Status.Should().Be(InvoiceStatus.Cancelled);
                timeout.Resolution.Should().Be(PaymentTimeoutResolution.InvoiceCancelled);
            }
        }
    }

    private static async Task Attempt(Func<Task> action)
    {
        // The losing customer payment is expected to conflict with an already cancelled invoice.
        try { await action(); } catch (BillingConflictException) { }
    }
    private BillingDbContext CreateContext() => new(new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql(infrastructure.Postgres.GetConnectionString()).Options);
    private IHost CreateHost() => Host.CreateDefaultBuilder()
        .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BillingDatabase"] = infrastructure.Postgres.GetConnectionString(),
            ["ConnectionStrings:Redis"] = infrastructure.Redis.GetConnectionString(),
            ["RabbitMq:Host"] = infrastructure.RabbitMq.Hostname,
            ["RabbitMq:Port"] = infrastructure.RabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:Username"] = "rabbitmq", ["RabbitMq:Password"] = "rabbitmq"
        })).ConfigureServices((context, services) =>
        {
            services.AddBillingApplication().AddBillingInfrastructure(context.Configuration);
            // The customer pays their own invoice; the admin flag only allows the ServiceIssue credit of the refund scenario.
            services.AddSingleton<IBillingActor>(new Actor(Customer, true));
        }).Build();
    private async Task<IHost> StartHost()
    {
        var host = CreateHost(); await host.Services.InitializeBillingDatabaseAsync(); await host.StartAsync(); return host;
    }
    private static async Task<Guid> PublishTimeout(IHost host, Guid shipment)
    {
        var eventId = Guid.NewGuid();
        await host.Services.GetRequiredService<IBus>().Publish(new ShipmentPaymentTimedOut(eventId, shipment, DateTimeOffset.UtcNow, shipment,
            "PF" + shipment.ToString("N")[..24].ToUpperInvariant()));
        return eventId;
    }
    private static async Task<Guid> SendPaymentRequested(IHost host, Guid shipment, decimal amount)
    {
        var eventId = Guid.NewGuid();
        var endpoint = await host.Services.GetRequiredService<IBus>().GetSendEndpoint(new Uri("queue:billing-payments"));
        // The registration timestamp stays fixed so a replay matches the immutable invoice details.
        await endpoint.Send(new PaymentRequested(eventId, shipment, new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero), shipment, Customer, amount, "EUR"));
        return eventId;
    }
    private static async Task<T> Send<T>(IHost host, IRequest<T> command)
    {
        await using var scope = host.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }
    private async Task WaitForInbox(Guid id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true) { await using var database = CreateContext(); if (await database.Set<InboxState>().AnyAsync(x => x.MessageId == id && x.Delivered != null, timeout.Token)) return; await Task.Delay(100, timeout.Token); }
    }
    private static async Task WaitUntil(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!predicate()) await Task.Delay(100, deadline.Token);
    }
}
