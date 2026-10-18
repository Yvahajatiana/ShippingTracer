using MediatR;
using ParcelFlow.Billing.Domain;

namespace ParcelFlow.Billing.Application;

public interface IBillingActor { string Subject { get; } bool IsAdmin { get; } }
public interface IBillingCommand { }
public interface IBillingStore
{
    Task LockInvoiceAsync(Guid id, CancellationToken cancellationToken);
    Task<Invoice?> FindInvoiceAsync(Guid id, CancellationToken cancellationToken);
    Task<PaymentAttempt?> FindPaymentByOperationAsync(Guid operationId, CancellationToken cancellationToken);
    Task<PaymentAttempt?> FindInvoicePaymentAsync(Guid invoiceId, CancellationToken cancellationToken);
    Task<CreditNote?> FindCreditNoteAsync(Guid operationId, CancellationToken cancellationToken);
    void AddInvoice(Invoice invoice);
    void AddPayment(PaymentAttempt payment);
    void AddCreditNote(CreditNote creditNote);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<InvoicePageDto> ListInvoicesAsync(string? ownerSubject, int page, int pageSize, CancellationToken cancellationToken);
    Task<CreditNotePageDto> ListCreditNotesAsync(Guid invoiceId, int page, int pageSize, CancellationToken cancellationToken);
}
public sealed record RegisterInvoice(Guid ShipmentId, string CustomerSubject, decimal Amount, string Currency, DateTimeOffset OccurredAtUtc) : IRequest<bool>, IBillingCommand;
public sealed record PayInvoice(Guid InvoiceId, Guid OperationId, MockPaymentOutcome Outcome) : IRequest<PaymentDto>, IBillingCommand;
public sealed record IssueCreditNote(Guid InvoiceId, Guid OperationId, decimal Amount, CreditReason Reason) : IRequest<CreditNoteDto>, IBillingCommand;
public sealed record GetInvoice(Guid InvoiceId) : IRequest<InvoiceDto>;
public sealed record ListInvoices(int Page = 1, int PageSize = 50) : IRequest<InvoicePageDto>;
public sealed record GetInvoicePayment(Guid InvoiceId) : IRequest<PaymentDto>;
public sealed record ListInvoiceCreditNotes(Guid InvoiceId, int Page = 1, int PageSize = 50) : IRequest<CreditNotePageDto>;
public sealed record GetCreditNote(Guid InvoiceId, Guid CreditNoteId) : IRequest<CreditNoteDto>;
public sealed record InvoiceDto(Guid Id, string Number, Guid ShipmentId, string CustomerSubject, decimal Amount, string Currency, DateTimeOffset IssuedAtUtc, InvoiceStatus Status, decimal CreditTotal, decimal RemainingCredit);
public sealed record PaymentDto(Guid OperationId, Guid InvoiceId, MockPaymentOutcome Outcome, decimal Amount, string Currency, DateTimeOffset OccurredAtUtc);
public sealed record CreditNoteDto(Guid Id, string Number, Guid InvoiceId, decimal Amount, string Currency, CreditReason Reason, DateTimeOffset OccurredAtUtc);
public sealed record InvoicePageDto(int Page, int PageSize, int Total, IReadOnlyList<InvoiceDto> Items);
public sealed record CreditNotePageDto(int Page, int PageSize, int Total, IReadOnlyList<CreditNoteDto> Items);
public sealed class BillingNotFoundException(string message) : Exception(message);
public sealed class BillingConflictException(string message) : Exception(message);
public sealed class BillingForbiddenException(string message) : Exception(message);
public static class BillingMapping
{
    public static InvoiceDto ToDto(this Invoice x) => new(x.Id, x.Number, x.ShipmentId, x.CustomerSubject, x.Amount, x.Currency, x.IssuedAtUtc, x.Status, x.CreditTotal, x.RemainingCredit);
    public static PaymentDto ToDto(this PaymentAttempt x) => new(x.OperationId, x.InvoiceId, x.Outcome, x.Amount, x.Currency, x.OccurredAtUtc);
    public static CreditNoteDto ToDto(this CreditNote x) => new(x.Id, x.Number, x.InvoiceId, x.Amount, x.Currency, x.Reason, x.OccurredAtUtc);
}
