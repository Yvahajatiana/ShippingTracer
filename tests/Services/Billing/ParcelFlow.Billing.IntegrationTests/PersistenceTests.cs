using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParcelFlow.Billing.Domain;
using ParcelFlow.Billing.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Billing.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceTests(InfrastructureFixture infrastructure)
{
    private BillingDbContext CreateContext() => new(new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql(infrastructure.Postgres.GetConnectionString()).Options);
    internal static Invoice Invoice(decimal amount = 12.90m, string owner = "billing-test-customer") => new(Guid.NewGuid(), owner, amount, "EUR", DateTimeOffset.UtcNow.AddTicks(7));

    [Fact]
    public async Task Migration_roundtrip_preserves_money_microseconds_and_required_ledger_relationships()
    {
        await using var database = CreateContext(); await database.Database.MigrateAsync();
        var invoice = Invoice(); invoice.RecordPayment(MockPaymentOutcome.Approved); invoice.IssueCredit(2.90m);
        var at = BillingRules.NormalizeTimestamp(DateTimeOffset.UtcNow.AddTicks(7));
        var payment = new PaymentAttempt(Guid.NewGuid(), invoice.Id, MockPaymentOutcome.Approved, invoice.Amount, "EUR", at, "customer");
        var credit = new CreditNote(Guid.NewGuid(), invoice.Id, 2.90m, "EUR", CreditReason.BillingCorrection, at, "admin");
        database.AddRange(invoice, payment, credit); await database.SaveChangesAsync(); database.ChangeTracker.Clear();
        var restored = await database.Invoices.SingleAsync(x => x.Id == invoice.Id);
        restored.Number.Should().Be(invoice.Number); restored.Amount.Should().Be(12.90m); restored.CreditTotal.Should().Be(2.90m);
        restored.Status.Should().Be(InvoiceStatus.PartiallyCredited); restored.RemainingCredit.Should().Be(10m);
        restored.IssuedAtUtc.Should().Be(invoice.IssuedAtUtc);
        (await database.Payments.SingleAsync(x => x.OperationId == payment.OperationId)).OccurredAtUtc.Should().Be(at);
        (await database.CreditNotes.SingleAsync(x => x.Id == credit.Id)).Number.Should().Be(credit.Number);
        (await database.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        var failure = await FluentActions.Awaiting(() => database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM billing_invoices WHERE \"Id\" = {invoice.Id}")).Should().ThrowAsync<PostgresException>();
        failure.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Required_invoice_foreign_keys_reject_orphan_payment_and_credit()
    {
        await using var database = CreateContext(); await database.Database.MigrateAsync();
        database.Payments.Add(new PaymentAttempt(Guid.NewGuid(), Guid.NewGuid(), MockPaymentOutcome.Approved, 10m, "EUR", DateTimeOffset.UtcNow, "actor"));
        var first = await FluentActions.Awaiting(() => database.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        first.Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        database.ChangeTracker.Clear();
        database.CreditNotes.Add(new CreditNote(Guid.NewGuid(), Guid.NewGuid(), 1m, "EUR", CreditReason.ServiceIssue, DateTimeOffset.UtcNow, "admin"));
        var second = await FluentActions.Awaiting(() => database.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        second.Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Database_unique_index_enforces_one_terminal_payment_per_invoice()
    {
        await using var database = CreateContext(); await database.Database.MigrateAsync(); var invoice = Invoice();
        database.Invoices.Add(invoice); database.Payments.Add(new PaymentAttempt(Guid.NewGuid(), invoice.Id, MockPaymentOutcome.Approved, invoice.Amount, "EUR", DateTimeOffset.UtcNow, "actor")); await database.SaveChangesAsync();
        database.Payments.Add(new PaymentAttempt(Guid.NewGuid(), invoice.Id, MockPaymentOutcome.Declined, invoice.Amount, "EUR", DateTimeOffset.UtcNow, "actor"));
        var failure = await FluentActions.Awaiting(() => database.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        failure.Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Invoice_xmin_rejects_stale_credit_writer()
    {
        await using var first = CreateContext(); await first.Database.MigrateAsync(); var invoice = Invoice(); invoice.RecordPayment(MockPaymentOutcome.Approved);
        first.Invoices.Add(invoice); await first.SaveChangesAsync();
        await using var second = CreateContext(); var stale = await second.Invoices.SingleAsync(x => x.Id == invoice.Id);
        invoice.IssueCredit(1m); await first.SaveChangesAsync(); stale.IssueCredit(2m);
        await FluentActions.Awaiting(() => second.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
