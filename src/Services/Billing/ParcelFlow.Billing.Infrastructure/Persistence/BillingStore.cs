using Microsoft.EntityFrameworkCore;
using ParcelFlow.Billing.Application;
using ParcelFlow.Billing.Domain;
using Polly;

namespace ParcelFlow.Billing.Infrastructure.Persistence;

public sealed class BillingStore(BillingDbContext database, ResiliencePipeline databaseReadPipeline) : IBillingStore
{
    public async Task LockInvoiceAsync(Guid id, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("Billing mutations require a transaction.");
        var key = "billing:invoice:" + id;
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }
    private IQueryable<T> Query<T>() where T : class => database.Database.CurrentTransaction is null ? database.Set<T>().AsNoTracking() : database.Set<T>();
    private async Task<T> Read<T>(Func<CancellationToken, Task<T>> read, CancellationToken token) =>
        database.Database.CurrentTransaction is not null ? await read(token) : await databaseReadPipeline.ExecuteAsync(async ct => await read(ct), token);
    public Task<Invoice?> FindInvoiceAsync(Guid id, CancellationToken token) => Read(ct => Query<Invoice>().SingleOrDefaultAsync(x => x.Id == id, ct), token);
    public Task<PaymentAttempt?> FindPaymentByOperationAsync(Guid id, CancellationToken token) => Read(ct => database.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == id, ct), token);
    public Task<PaymentAttempt?> FindInvoicePaymentAsync(Guid id, CancellationToken token) => Read(ct => database.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.InvoiceId == id, ct), token);
    public Task<CreditNote?> FindCreditNoteAsync(Guid id, CancellationToken token) => Read(ct => database.CreditNotes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct), token);
    public void AddInvoice(Invoice invoice) => database.Invoices.Add(invoice);
    public void AddPayment(PaymentAttempt payment) => database.Payments.Add(payment);
    public void AddCreditNote(CreditNote creditNote) => database.CreditNotes.Add(creditNote);
    public async Task SaveChangesAsync(CancellationToken token) => await database.SaveChangesAsync(token);
    public Task<InvoicePageDto> ListInvoicesAsync(string? ownerSubject, int page, int pageSize, CancellationToken token) => Read(async ct =>
    {
        var query = database.Invoices.AsNoTracking();
        if (ownerSubject is not null) query = query.Where(x => x.CustomerSubject == ownerSubject);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.IssuedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new InvoicePageDto(page, pageSize, total, items.Select(x => x.ToDto()).ToArray());
    }, token);
    public Task<CreditNotePageDto> ListCreditNotesAsync(Guid invoiceId, int page, int pageSize, CancellationToken token) => Read(async ct =>
    {
        var query = database.CreditNotes.AsNoTracking().Where(x => x.InvoiceId == invoiceId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.OccurredAtUtc).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new CreditNotePageDto(page, pageSize, total, items.Select(x => x.ToDto()).ToArray());
    }, token);
}
