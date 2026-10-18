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
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;
using Xunit;

namespace ParcelFlow.Billing.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceConsumerTests(InfrastructureFixture infrastructure)
{
    private BillingDbContext CreateContext() => new(new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql(infrastructure.Postgres.GetConnectionString()).Options);
    private sealed record Actor(string Subject, bool IsAdmin) : IBillingActor;
    private IHost CreateHost(string subject = "billing-test-admin", bool admin = true) => Host.CreateDefaultBuilder()
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
            services.AddSingleton<IBillingActor>(new Actor(subject, admin));
        }).Build();
    private static async Task<T> Send<T>(IHost host, IRequest<T> command)
    {
        await using var scope = host.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }
    private static async Task<Guid> Invoice(IHost host, decimal amount = 12.90m, string owner = "billing-test-customer")
    {
        var id = Guid.NewGuid(); await Send(host, new RegisterInvoice(id, owner, amount, "EUR", DateTimeOffset.UtcNow)); return id;
    }

    [Fact]
    public async Task Demo_seed_is_opt_in_idempotent_and_contains_no_fabricated_payment_credit_or_events()
    {
        using var host = CreateHost(); await host.Services.InitializeBillingDatabaseAsync();
        host.Services.GetRequiredService<IConfiguration>()["Database:SeedDemoData"] = "true";
        await host.Services.InitializeBillingDatabaseAsync(); await host.Services.InitializeBillingDatabaseAsync();
        await using var database = CreateContext();
        var ids = new[] { Guid.Parse("71000000-0000-0000-0000-000000000001"), Guid.Parse("71000000-0000-0000-0000-000000000002") };
        var invoices = await database.Invoices.Where(x => ids.Contains(x.Id)).ToListAsync();
        invoices.Should().HaveCount(2).And.OnlyContain(x => x.Status == InvoiceStatus.PendingPayment && x.CreditTotal == 0m);
        invoices.Select(x => x.Amount).Should().BeEquivalentTo([12.90m, 18.50m]);
        (await database.Payments.AnyAsync(x => ids.Contains(x.InvoiceId))).Should().BeFalse();
        (await database.CreditNotes.AnyAsync(x => ids.Contains(x.InvoiceId))).Should().BeFalse();
        foreach (var id in ids) (await database.Set<OutboxMessage>().AnyAsync(x => x.Body.Contains(id.ToString()))).Should().BeFalse();
    }

    [Fact]
    public async Task Payment_requested_sent_before_billing_start_is_durable_and_duplicate_never_resets_paid_credited_invoice()
    {
        var shipment = Guid.NewGuid(); var occurred = DateTimeOffset.UtcNow.AddTicks(7);
        var request = new PaymentRequested(Guid.NewGuid(), shipment, occurred, shipment, "billing-test-customer", 12.90m, "EUR");
        var producer = Bus.Factory.CreateUsingRabbitMq(rabbit => rabbit.Host(infrastructure.RabbitMq.Hostname,
            infrastructure.RabbitMq.GetMappedPublicPort(5672), "/", host => { host.Username("rabbitmq"); host.Password("rabbitmq"); }));
        await producer.StartAsync();
        try
        {
            var endpoint = await producer.GetSendEndpoint(new Uri("queue:billing-payments"));
            var first = Guid.NewGuid(); await endpoint.Send(request, context => context.MessageId = first);
            using var billing = CreateHost(); await billing.Services.InitializeBillingDatabaseAsync(); await billing.StartAsync();
            try
            {
                await WaitForInbox(first);
                await using (var database = CreateContext())
                {
                    var invoice = await database.Invoices.SingleAsync(x => x.Id == shipment);
                    invoice.Status.Should().Be(InvoiceStatus.PendingPayment); invoice.IssuedAtUtc.Should().Be(BillingRules.NormalizeTimestamp(occurred));
                    (await database.Payments.AnyAsync(x => x.InvoiceId == shipment)).Should().BeFalse();
                }
                await Send(billing, new PayInvoice(shipment, Guid.NewGuid(), MockPaymentOutcome.Approved));
                await Send(billing, new IssueCreditNote(shipment, Guid.NewGuid(), 2.90m, CreditReason.ServiceIssue));
                foreach (var _ in Enumerable.Range(0, 2)) { var message = Guid.NewGuid(); await endpoint.Send(request, context => context.MessageId = message); await WaitForInbox(message); }
                await using var final = CreateContext(); var paid = await final.Invoices.SingleAsync(x => x.Id == shipment);
                paid.Status.Should().Be(InvoiceStatus.PartiallyCredited); paid.CreditTotal.Should().Be(2.90m);
                (await final.Payments.CountAsync(x => x.InvoiceId == shipment)).Should().Be(1);
                (await final.CreditNotes.CountAsync(x => x.InvoiceId == shipment)).Should().Be(1);
            }
            finally { await billing.StopAsync(); }
        }
        finally { await producer.StopAsync(); }
    }

    [Fact]
    public async Task Twenty_concurrent_payments_commit_one_terminal_attempt_and_one_outbox_event()
    {
        using var host = CreateHost(); await host.Services.InitializeBillingDatabaseAsync(); var invoice = await Invoice(host);
        var attempts = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();
        var results = await Task.WhenAll(attempts.Select(async operation =>
        {
            try { await Send(host, new PayInvoice(invoice, operation, MockPaymentOutcome.Approved)); return true; }
            catch (BillingConflictException) { return false; }
        }));
        results.Count(x => x).Should().Be(1);
        await using var database = CreateContext(); var payment = await database.Payments.SingleAsync(x => x.InvoiceId == invoice);
        (await database.Invoices.SingleAsync(x => x.Id == invoice)).Status.Should().Be(InvoiceStatus.Paid);
        (await database.Set<OutboxMessage>().CountAsync(x => x.Body.Contains(payment.OperationId.ToString()))).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_credits_never_exceed_paid_amount_and_replays_do_not_emit_twice()
    {
        using var host = CreateHost(); await host.Services.InitializeBillingDatabaseAsync(); var invoice = await Invoice(host, 10m);
        await Send(host, new PayInvoice(invoice, Guid.NewGuid(), MockPaymentOutcome.Approved));
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            var command = new IssueCreditNote(invoice, Guid.NewGuid(), 3m, CreditReason.ServiceIssue);
            try { await Send(host, command); await Send(host, command); return true; }
            catch (BillingConflictException) { return false; }
        }));
        results.Count(x => x).Should().Be(3);
        await using var database = CreateContext(); var stored = await database.Invoices.SingleAsync(x => x.Id == invoice);
        stored.CreditTotal.Should().Be(9m); stored.RemainingCredit.Should().Be(1m);
        var credits = await database.CreditNotes.Where(x => x.InvoiceId == invoice).ToListAsync(); credits.Should().HaveCount(3);
        foreach (var credit in credits) (await database.Set<OutboxMessage>().CountAsync(x => x.Body.Contains(credit.Id.ToString()))).Should().Be(1);
    }

    [Fact]
    public async Task Reused_operation_on_another_invoice_conflicts_without_mutating_target()
    {
        using var host = CreateHost(); await host.Services.InitializeBillingDatabaseAsync(); var first = await Invoice(host); var second = await Invoice(host);
        var operation = Guid.NewGuid(); await Send(host, new PayInvoice(first, operation, MockPaymentOutcome.Approved));
        await FluentActions.Awaiting(() => Send(host, new PayInvoice(second, operation, MockPaymentOutcome.Approved))).Should().ThrowAsync<BillingConflictException>();
        await using var database = CreateContext(); (await database.Invoices.SingleAsync(x => x.Id == second)).Status.Should().Be(InvoiceStatus.PendingPayment);
        await Send(host, new PayInvoice(second, Guid.NewGuid(), MockPaymentOutcome.Approved));
        var creditId = Guid.NewGuid(); await Send(host, new IssueCreditNote(first, creditId, 1m, CreditReason.BillingCorrection));
        await FluentActions.Awaiting(() => Send(host, new IssueCreditNote(second, creditId, 1m, CreditReason.BillingCorrection))).Should().ThrowAsync<BillingConflictException>();
        database.ChangeTracker.Clear(); (await database.Invoices.SingleAsync(x => x.Id == second)).CreditTotal.Should().Be(0m);
    }

    [Fact]
    public async Task Financial_lists_are_scoped_to_actual_owner_in_database_queries()
    {
        var owner = "owner-" + Guid.NewGuid().ToString("N"); var other = "other-" + Guid.NewGuid().ToString("N");
        using var customer = CreateHost(owner, false); await customer.Services.InitializeBillingDatabaseAsync();
        var own = await Invoice(customer, owner: owner); await Invoice(customer, owner: other);
        var page = await Send(customer, new ListInvoices()); page.Total.Should().Be(1); page.Items.Should().ContainSingle(x => x.Id == own);
        await Send(customer, new PayInvoice(own, Guid.NewGuid(), MockPaymentOutcome.Approved));
        using var admin = CreateHost(); var credit = await Send(admin, new IssueCreditNote(own, Guid.NewGuid(), 1m, CreditReason.ServiceIssue));
        var notes = await Send(customer, new ListInvoiceCreditNotes(own)); notes.Items.Should().ContainSingle(x => x.Id == credit.Id);
    }

    [Fact]
    public async Task Payment_failure_after_invoice_mutation_rolls_back_ledger_and_outbox()
    {
        using var host = CreateHost(); await host.Services.InitializeBillingDatabaseAsync(); var invoice = await Invoice(host);
        await using var database = CreateContext();
        await database.Database.ExecuteSqlRawAsync("ALTER TABLE billing_payments ADD CONSTRAINT test_billing_payment_rollback CHECK (\"ActorSubject\" <> 'billing-test-admin') NOT VALID");
        var operation = Guid.NewGuid();
        try
        {
            await FluentActions.Awaiting(() => Send(host, new PayInvoice(invoice, operation, MockPaymentOutcome.Approved))).Should().ThrowAsync<DbUpdateException>();
            await using var verify = CreateContext(); var stored = await verify.Invoices.SingleAsync(x => x.Id == invoice);
            stored.Status.Should().Be(InvoiceStatus.PendingPayment); stored.LastActionAtUtc.Should().Be(stored.IssuedAtUtc);
            (await verify.Payments.AnyAsync(x => x.OperationId == operation)).Should().BeFalse();
            (await verify.Set<OutboxMessage>().AnyAsync(x => x.Body.Contains(operation.ToString()))).Should().BeFalse();
        }
        finally { await database.Database.ExecuteSqlRawAsync("ALTER TABLE billing_payments DROP CONSTRAINT test_billing_payment_rollback"); }
    }

    [Fact]
    public async Task Credit_failure_after_invoice_mutation_rolls_back_balance_ledger_and_outbox()
    {
        using var host = CreateHost(); await host.Services.InitializeBillingDatabaseAsync(); var invoice = await Invoice(host);
        await Send(host, new PayInvoice(invoice, Guid.NewGuid(), MockPaymentOutcome.Approved));
        await using var database = CreateContext();
        await database.Database.ExecuteSqlRawAsync("ALTER TABLE billing_credit_notes ADD CONSTRAINT test_billing_credit_rollback CHECK (\"ActorSubject\" <> 'billing-test-admin') NOT VALID");
        var operation = Guid.NewGuid();
        try
        {
            await FluentActions.Awaiting(() => Send(host, new IssueCreditNote(invoice, operation, 1m, CreditReason.ServiceIssue))).Should().ThrowAsync<DbUpdateException>();
            await using var verify = CreateContext(); var stored = await verify.Invoices.SingleAsync(x => x.Id == invoice);
            stored.Status.Should().Be(InvoiceStatus.Paid); stored.CreditTotal.Should().Be(0m);
            (await verify.CreditNotes.AnyAsync(x => x.Id == operation)).Should().BeFalse();
            (await verify.Set<OutboxMessage>().AnyAsync(x => x.Body.Contains(operation.ToString()))).Should().BeFalse();
        }
        finally { await database.Database.ExecuteSqlRawAsync("ALTER TABLE billing_credit_notes DROP CONSTRAINT test_billing_credit_rollback"); }
    }

    [Fact]
    public async Task Registration_failure_rolls_back_invoice_and_invoice_issued_event()
    {
        using var host = CreateHost(); await host.Services.InitializeBillingDatabaseAsync();
        await using var database = CreateContext();
        await database.Database.ExecuteSqlRawAsync("ALTER TABLE billing_invoices ADD CONSTRAINT test_billing_invoice_rollback CHECK (\"CustomerSubject\" <> 'rollback-owner') NOT VALID");
        var invoice = Guid.NewGuid();
        try
        {
            await FluentActions.Awaiting(() => Send(host, new RegisterInvoice(invoice, "rollback-owner", 10m, "EUR", DateTimeOffset.UtcNow))).Should().ThrowAsync<DbUpdateException>();
            await using var verify = CreateContext(); (await verify.Invoices.AnyAsync(x => x.Id == invoice)).Should().BeFalse();
            (await verify.Set<OutboxMessage>().AnyAsync(x => x.Body.Contains(invoice.ToString()))).Should().BeFalse();
        }
        finally { await database.Database.ExecuteSqlRawAsync("ALTER TABLE billing_invoices DROP CONSTRAINT test_billing_invoice_rollback"); }
    }

    [Fact]
    public async Task Conflicting_registration_faults_and_preserves_original_invoice()
    {
        using var host = CreateHost(); await host.Services.InitializeBillingDatabaseAsync(); await host.StartAsync();
        var invoice = await Invoice(host); var bus = host.Services.GetRequiredService<IBus>();
        var fault = new TaskCompletionSource<Fault<PaymentRequested>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = bus.ConnectReceiveEndpoint("billing-fault-" + Guid.NewGuid().ToString("N"), endpoint => endpoint.Handler<Fault<PaymentRequested>>(context =>
        { if (context.Message.Message.ShipmentId == invoice) fault.TrySetResult(context.Message); return Task.CompletedTask; }));
        await observer.Ready;
        try
        {
            var endpoint = await bus.GetSendEndpoint(new Uri("queue:billing-payments"));
            await endpoint.Send(new PaymentRequested(Guid.NewGuid(), invoice, DateTimeOffset.UtcNow, invoice, "changed-owner", 99m, "EUR"));
            var result = await fault.Task.WaitAsync(TimeSpan.FromSeconds(30)); result.Exceptions.Should().NotBeEmpty();
            await using var database = CreateContext(); var original = await database.Invoices.SingleAsync(x => x.Id == invoice);
            original.CustomerSubject.Should().Be("billing-test-customer"); original.Amount.Should().Be(12.90m); original.Status.Should().Be(InvoiceStatus.PendingPayment);
        }
        finally { await observer.StopAsync(); await host.StopAsync(); }
    }
    private async Task WaitForInbox(Guid id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true) { await using var database = CreateContext(); if (await database.Set<InboxState>().AnyAsync(x => x.MessageId == id && x.Delivered != null, timeout.Token)) return; await Task.Delay(100, timeout.Token); }
    }
}
