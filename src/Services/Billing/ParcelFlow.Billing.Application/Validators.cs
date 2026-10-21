using FluentValidation;
using ParcelFlow.Billing.Domain;

namespace ParcelFlow.Billing.Application;

public sealed class RegisterInvoiceValidator : AbstractValidator<RegisterInvoice>
{
    public RegisterInvoiceValidator()
    {
        RuleFor(x => x.ShipmentId).NotEmpty(); RuleFor(x => x.CustomerSubject).Must(BillingRules.IsSubject);
        RuleFor(x => x.Amount).Must(BillingRules.IsAmount); RuleFor(x => x.Currency).Equal("EUR"); RuleFor(x => x.OccurredAtUtc).NotEqual(default(DateTimeOffset));
    }
}
public sealed class PayInvoiceValidator : AbstractValidator<PayInvoice>
{
    public PayInvoiceValidator() { RuleFor(x => x.InvoiceId).NotEmpty(); RuleFor(x => x.OperationId).NotEmpty(); RuleFor(x => x.Outcome).IsInEnum(); }
}
public sealed class IssueCreditNoteValidator : AbstractValidator<IssueCreditNote>
{
    public IssueCreditNoteValidator() { RuleFor(x => x.InvoiceId).NotEmpty(); RuleFor(x => x.OperationId).NotEmpty(); RuleFor(x => x.Amount).Must(BillingRules.IsAmount); RuleFor(x => x.Reason).IsInEnum(); }
}
public sealed class GetInvoiceValidator : AbstractValidator<GetInvoice> { public GetInvoiceValidator() { RuleFor(x => x.InvoiceId).NotEmpty(); } }
public sealed class GetInvoicePaymentValidator : AbstractValidator<GetInvoicePayment> { public GetInvoicePaymentValidator() { RuleFor(x => x.InvoiceId).NotEmpty(); } }
public sealed class GetCreditNoteValidator : AbstractValidator<GetCreditNote> { public GetCreditNoteValidator() { RuleFor(x => x.InvoiceId).NotEmpty(); RuleFor(x => x.CreditNoteId).NotEmpty(); } }
public sealed class ListInvoicesValidator : AbstractValidator<ListInvoices> { public ListInvoicesValidator() { RuleFor(x => x.Page).InclusiveBetween(1, 1000000); RuleFor(x => x.PageSize).InclusiveBetween(1, 100); } }
public sealed class ListInvoiceCreditNotesValidator : AbstractValidator<ListInvoiceCreditNotes> { public ListInvoiceCreditNotesValidator() { RuleFor(x => x.InvoiceId).NotEmpty(); RuleFor(x => x.Page).InclusiveBetween(1, 1000000); RuleFor(x => x.PageSize).InclusiveBetween(1, 100); } }
