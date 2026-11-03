using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Application.Correlation;
using ParcelFlow.BuildingBlocks.Application.Identity;
using ParcelFlow.Billing.Domain;
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;

namespace ParcelFlow.Billing.Application;

internal static class HandlerRules
{
    public static void Authorize(Invoice invoice, IBillingActor actor)
    {
        if (!BillingRules.IsSubject(actor.Subject) || (!actor.IsAdmin && invoice.CustomerSubject != actor.Subject))
            throw new BillingForbiddenException("Only the invoice owner or an administrator may access this invoice.");
    }
    public static void Admin(IBillingActor actor)
    {
        if (!actor.IsAdmin || !BillingRules.IsSubject(actor.Subject)) throw new BillingForbiddenException("An administrator is required.");
    }
    public static async Task<Invoice> Invoice(IBillingStore store, Guid id, CancellationToken ct) => await store.FindInvoiceAsync(id, ct) ?? throw new BillingNotFoundException("Invoice not found.");
    public static void Apply(Action action) { try { action(); } catch (InvalidOperationException ex) { throw new BillingConflictException(ex.Message); } }
}
public sealed class RegisterInvoiceHandler(IBillingStore store, IPublishEndpoint publisher, ICorrelationContext correlation) : IRequestHandler<RegisterInvoice, bool>
{
    public async Task<bool> Handle(RegisterInvoice request, CancellationToken ct)
    {
        await store.LockInvoiceAsync(request.ShipmentId, ct);
        var invoice = await store.FindInvoiceAsync(request.ShipmentId, ct);
        if (invoice is not null)
        {
            if (!invoice.Matches(request.CustomerSubject, request.Amount, request.Currency, request.OccurredAtUtc)) throw new BillingConflictException("The invoice registration conflicts with its immutable shipment details.");
            return false;
        }
        invoice = new Invoice(request.ShipmentId, request.CustomerSubject, request.Amount, request.Currency, request.OccurredAtUtc);
        store.AddInvoice(invoice);
        await publisher.Publish(new InvoiceIssued(DeterministicGuid.Create("billing:invoice-issued:", invoice.Id), correlation.CorrelationId, invoice.IssuedAtUtc, invoice.Id, invoice.ShipmentId, invoice.CustomerSubject, invoice.Amount, invoice.Currency), ct);
        await store.SaveChangesAsync(ct);
        return true;
    }
}
public sealed class PayInvoiceHandler(IBillingStore store, IBillingActor actor, IPublishEndpoint publisher, TimeProvider clock) : IRequestHandler<PayInvoice, PaymentDto>
{
    public async Task<PaymentDto> Handle(PayInvoice request, CancellationToken ct)
    {
        await store.LockInvoiceAsync(request.InvoiceId, ct);
        var invoice = await HandlerRules.Invoice(store, request.InvoiceId, ct); HandlerRules.Authorize(invoice, actor);
        var existing = await store.FindPaymentByOperationAsync(request.OperationId, ct);
        if (existing is not null)
        {
            if (!existing.Matches(invoice.Id, request.Outcome, actor.Subject)) throw new BillingConflictException("The payment operation identifier has already been used with different details.");
            return existing.ToDto();
        }
        if (await store.FindInvoicePaymentAsync(invoice.Id, ct) is not null) throw new BillingConflictException("This invoice already has a terminal payment attempt.");
        var now = invoice.RecordAction(clock.GetUtcNow());
        var payment = new PaymentAttempt(request.OperationId, invoice.Id, request.Outcome, invoice.Amount, invoice.Currency, now, actor.Subject);
        HandlerRules.Apply(() => invoice.RecordPayment(request.Outcome)); store.AddPayment(payment);
        if (request.Outcome == MockPaymentOutcome.Approved)
            await publisher.Publish(new PaymentCompleted(PaymentEventId("billing:payment-completed:", invoice, payment), invoice.ShipmentId, now, invoice.ShipmentId, invoice.Amount, invoice.Currency), ct);
        else
            await publisher.Publish(new PaymentFailed(PaymentEventId("billing:payment-failed:", invoice, payment), invoice.ShipmentId, now, invoice.ShipmentId, "Mock payment declined."), ct);
        await store.SaveChangesAsync(ct); return payment.ToDto();
    }
    // The client-supplied OperationId is scoped by event type and invoice: a retry on the same invoice keeps its EventId,
    // while the same key reused on another invoice (or another service) can never collide in consumers or the Inbox.
    private static Guid PaymentEventId(string scope, Invoice invoice, PaymentAttempt payment) =>
        DeterministicGuid.Create(scope, invoice.Id, payment.OperationId);
}
public sealed class IssueCreditNoteHandler(IBillingStore store, IBillingActor actor, IPublishEndpoint publisher, TimeProvider clock) : IRequestHandler<IssueCreditNote, CreditNoteDto>
{
    public async Task<CreditNoteDto> Handle(IssueCreditNote request, CancellationToken ct)
    {
        HandlerRules.Admin(actor);
        await store.LockInvoiceAsync(request.InvoiceId, ct);
        var invoice = await HandlerRules.Invoice(store, request.InvoiceId, ct);
        var existing = await store.FindCreditNoteAsync(request.OperationId, ct);
        if (existing is not null)
        {
            if (!existing.Matches(invoice.Id, request.Amount, request.Reason, actor.Subject)) throw new BillingConflictException("The credit operation identifier has already been used with different details.");
            return existing.ToDto();
        }
        var now = invoice.RecordAction(clock.GetUtcNow());
        var credit = new CreditNote(request.OperationId, invoice.Id, request.Amount, invoice.Currency, request.Reason, now, actor.Subject);
        HandlerRules.Apply(() => invoice.IssueCredit(request.Amount)); store.AddCreditNote(credit);
        await publisher.Publish(new CreditNoteIssued(DeterministicGuid.Create("billing:credit-note-issued:", credit.Id), invoice.ShipmentId, now, credit.Id, invoice.Id, invoice.ShipmentId, invoice.CustomerSubject, credit.Amount, credit.Currency), ct);
        await store.SaveChangesAsync(ct); return credit.ToDto();
    }
}
public sealed class GetInvoiceHandler(IBillingStore store, IBillingActor actor) : IRequestHandler<GetInvoice, InvoiceDto>
{
    public async Task<InvoiceDto> Handle(GetInvoice request, CancellationToken ct)
    {
        var invoice = await HandlerRules.Invoice(store, request.InvoiceId, ct); HandlerRules.Authorize(invoice, actor); return invoice.ToDto();
    }
}
public sealed class ListInvoicesHandler(IBillingStore store, IBillingActor actor) : IRequestHandler<ListInvoices, InvoicePageDto>
{
    public Task<InvoicePageDto> Handle(ListInvoices request, CancellationToken ct)
    {
        if (!BillingRules.IsSubject(actor.Subject)) throw new BillingForbiddenException("A valid actor is required.");
        return store.ListInvoicesAsync(actor.IsAdmin ? null : actor.Subject, request.Page, request.PageSize, ct);
    }
}
public sealed class GetInvoicePaymentHandler(IBillingStore store, IBillingActor actor) : IRequestHandler<GetInvoicePayment, PaymentDto>
{
    public async Task<PaymentDto> Handle(GetInvoicePayment request, CancellationToken ct)
    {
        var invoice = await HandlerRules.Invoice(store, request.InvoiceId, ct); HandlerRules.Authorize(invoice, actor);
        return (await store.FindInvoicePaymentAsync(invoice.Id, ct) ?? throw new BillingNotFoundException("Payment not found.")).ToDto();
    }
}
public sealed class ListInvoiceCreditNotesHandler(IBillingStore store, IBillingActor actor) : IRequestHandler<ListInvoiceCreditNotes, CreditNotePageDto>
{
    public async Task<CreditNotePageDto> Handle(ListInvoiceCreditNotes request, CancellationToken ct)
    {
        var invoice = await HandlerRules.Invoice(store, request.InvoiceId, ct); HandlerRules.Authorize(invoice, actor);
        return await store.ListCreditNotesAsync(invoice.Id, request.Page, request.PageSize, ct);
    }
}
public sealed class GetCreditNoteHandler(IBillingStore store, IBillingActor actor) : IRequestHandler<GetCreditNote, CreditNoteDto>
{
    public async Task<CreditNoteDto> Handle(GetCreditNote request, CancellationToken ct)
    {
        var invoice = await HandlerRules.Invoice(store, request.InvoiceId, ct); HandlerRules.Authorize(invoice, actor);
        var credit = await store.FindCreditNoteAsync(request.CreditNoteId, ct);
        if (credit is null || credit.InvoiceId != invoice.Id) throw new BillingNotFoundException("Credit note not found.");
        return credit.ToDto();
    }
}
