using System.Reflection;
using FluentAssertions;
using MassTransit;
using ParcelFlow.Billing.Application;
using ParcelFlow.Billing.Domain;
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;
using Xunit;

namespace ParcelFlow.Billing.UnitTests;

public sealed class BillingTests
{
    private static readonly DateTimeOffset Issued = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.001")]
    [InlineData("1000000.01")]
    public void Money_must_be_positive_bounded_and_exact_cents(string value)
    {
        var amount = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        FluentActions.Invoking(() => Invoice(amount)).Should().Throw<ArgumentException>();
        new RegisterInvoiceValidator().Validate(new RegisterInvoice(Guid.NewGuid(), "alice", amount, "EUR", Issued)).IsValid.Should().BeFalse();
        new IssueCreditNoteValidator().Validate(new IssueCreditNote(Guid.NewGuid(), Guid.NewGuid(), amount, CreditReason.BillingCorrection)).IsValid.Should().BeFalse();
    }
    [Theory]
    [InlineData(InvoiceStatus.PendingPayment)]
    [InlineData(InvoiceStatus.PaymentFailed)]
    public void Unpaid_or_declined_invoices_cannot_receive_credit(InvoiceStatus status)
    {
        var invoice = Invoice(); if (status == InvoiceStatus.PaymentFailed) invoice.RecordPayment(MockPaymentOutcome.Declined);
        FluentActions.Invoking(() => invoice.IssueCredit(1m)).Should().Throw<InvalidOperationException>(); invoice.RemainingCredit.Should().Be(0);
    }
    [Fact]
    public void Partial_then_full_credit_preserves_exact_remaining_balance()
    {
        var invoice = Invoice(); invoice.RecordPayment(MockPaymentOutcome.Approved); invoice.IssueCredit(3.33m);
        invoice.Status.Should().Be(InvoiceStatus.PartiallyCredited); invoice.RemainingCredit.Should().Be(6.67m);
        FluentActions.Invoking(() => invoice.IssueCredit(6.68m)).Should().Throw<InvalidOperationException>();
        invoice.IssueCredit(6.67m); invoice.Status.Should().Be(InvoiceStatus.Credited); invoice.CreditTotal.Should().Be(10m); invoice.RemainingCredit.Should().Be(0);
        FluentActions.Invoking(() => invoice.IssueCredit(.01m)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => invoice.RecordPayment(MockPaymentOutcome.Approved)).Should().Throw<InvalidOperationException>();
    }
    [Fact]
    public void Subject_currency_enums_and_identifiers_are_domain_invariants()
    {
        FluentActions.Invoking(() => new Invoice(Guid.Empty, "alice", 10, "EUR", Issued)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new Invoice(Guid.NewGuid(), "alice\nadmin", 10, "EUR", Issued)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new Invoice(Guid.NewGuid(), " alice", 10, "EUR", Issued)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new Invoice(Guid.NewGuid(), "alice", 10, "USD", Issued)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Invoice().RecordPayment((MockPaymentOutcome)9)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new CreditNote(Guid.NewGuid(), Guid.NewGuid(), 1, "EUR", (CreditReason)99, Issued, "admin")).Should().Throw<ArgumentOutOfRangeException>();
    }
    [Fact]
    public void Timestamps_are_UTC_microseconds_strictly_monotonic_even_after_clock_regression()
    {
        var offset = Issued.ToOffset(TimeSpan.FromHours(3)).AddTicks(7); var invoice = new Invoice(Guid.NewGuid(), "alice", 10, "EUR", offset);
        invoice.IssuedAtUtc.Should().Be(Issued); invoice.IssuedAtUtc.Offset.Should().Be(TimeSpan.Zero);
        invoice.RecordAction(offset.AddDays(-1)).Should().Be(Issued.AddTicks(10)); invoice.RecordAction(offset).Should().Be(Issued.AddTicks(20));
        var future = Issued.AddHours(2).AddTicks(39); invoice.RecordAction(future).Ticks.Should().Be(future.Ticks - 9);
    }
    [Fact]
    public async Task Registration_is_immutable_and_replay_does_not_reset_paid_or_credited_state()
    {
        var f = Fixture(false); var handler = new RegisterInvoiceHandler(f.Store, f.Endpoint);
        var request = new RegisterInvoice(Guid.NewGuid(), "alice", 10m, "EUR", Issued.AddTicks(7));
        (await handler.Handle(request, default)).Should().BeTrue(); var invoice = f.Store.Invoices.Single(); invoice.RecordPayment(MockPaymentOutcome.Approved); invoice.IssueCredit(2m);
        (await handler.Handle(request with { OccurredAtUtc = Issued }, default)).Should().BeFalse(); invoice.Status.Should().Be(InvoiceStatus.PartiallyCredited);
        f.Store.Saves.Should().Be(1); f.Publisher.Events.Should().ContainSingle().Which.Should().BeOfType<InvoiceIssued>();
        await FluentActions.Awaiting(() => handler.Handle(request with { Amount = 11 }, default)).Should().ThrowAsync<BillingConflictException>();
        await FluentActions.Awaiting(() => handler.Handle(request with { CustomerSubject = "mallory" }, default)).Should().ThrowAsync<BillingConflictException>();
        await FluentActions.Awaiting(() => handler.Handle(request with { OccurredAtUtc = Issued.AddSeconds(1) }, default)).Should().ThrowAsync<BillingConflictException>();
    }
    [Fact]
    public async Task Approved_payment_publishes_exact_amount_once_and_replays_after_credit()
    {
        var f = Fixture(); var operation = Guid.NewGuid(); var request = new PayInvoice(f.Invoice.Id, operation, MockPaymentOutcome.Approved);
        var handler = new PayInvoiceHandler(f.Store, new Actor("alice"), f.Endpoint, new Clock());
        var paid = await handler.Handle(request, default); f.Invoice.IssueCredit(1m);
        (await handler.Handle(request, default)).Should().Be(paid); f.Store.Saves.Should().Be(1); f.Publisher.Events.Should().ContainSingle();
        var fact = f.Publisher.Events.OfType<PaymentCompleted>().Single(); fact.EventId.Should().Be(operation); fact.ShipmentId.Should().Be(f.Invoice.Id); fact.Amount.Should().Be(10); fact.Currency.Should().Be("EUR");
        await FluentActions.Awaiting(() => handler.Handle(request with { OperationId = Guid.NewGuid() }, default)).Should().ThrowAsync<BillingConflictException>();
        await FluentActions.Awaiting(() => handler.Handle(request with { Outcome = MockPaymentOutcome.Declined }, default)).Should().ThrowAsync<BillingConflictException>();
        await FluentActions.Awaiting(() => new PayInvoiceHandler(f.Store, new Actor("admin", true), f.Endpoint, new Clock()).Handle(request, default)).Should().ThrowAsync<BillingConflictException>();
    }
    [Fact]
    public async Task Declined_payment_is_terminal_and_replay_is_successful_without_republishing()
    {
        var f = Fixture(); var handler = new PayInvoiceHandler(f.Store, new Actor("alice"), f.Endpoint, new Clock());
        var request = new PayInvoice(f.Invoice.Id, Guid.NewGuid(), MockPaymentOutcome.Declined);
        var declined = await handler.Handle(request, default); (await handler.Handle(request, default)).Should().Be(declined);
        f.Invoice.Status.Should().Be(InvoiceStatus.PaymentFailed); f.Publisher.Events.Should().ContainSingle().Which.Should().BeOfType<PaymentFailed>();
        await FluentActions.Awaiting(() => handler.Handle(request with { OperationId = Guid.NewGuid(), Outcome = MockPaymentOutcome.Approved }, default)).Should().ThrowAsync<BillingConflictException>();
    }
    [Fact]
    public async Task Operation_identifier_cannot_be_reused_for_another_invoice()
    {
        var f = Fixture(); var other = Invoice(); f.Store.Invoices.Add(other); var operation = Guid.NewGuid();
        var pay = new PayInvoiceHandler(f.Store, new Actor("alice"), f.Endpoint, new Clock());
        await pay.Handle(new(f.Invoice.Id, operation, MockPaymentOutcome.Approved), default);
        await FluentActions.Awaiting(() => pay.Handle(new(other.Id, operation, MockPaymentOutcome.Approved), default)).Should().ThrowAsync<BillingConflictException>();
        other.Status.Should().Be(InvoiceStatus.PendingPayment);
    }
    [Fact]
    public async Task Credit_replay_uses_exact_payload_and_actor_even_after_fully_credited()
    {
        var f = Fixture(); f.Invoice.RecordPayment(MockPaymentOutcome.Approved);
        var handler = new IssueCreditNoteHandler(f.Store, new Actor("admin", true), f.Endpoint, new Clock());
        var request = new IssueCreditNote(f.Invoice.Id, Guid.NewGuid(), 10m, CreditReason.ServiceIssue);
        var credit = await handler.Handle(request, default); (await handler.Handle(request, default)).Should().Be(credit);
        f.Invoice.Status.Should().Be(InvoiceStatus.Credited); f.Store.Saves.Should().Be(1); f.Publisher.Events.Should().ContainSingle();
        var fact = f.Publisher.Events.OfType<CreditNoteIssued>().Single(); fact.EventId.Should().NotBeEmpty(); fact.CreditNoteId.Should().Be(request.OperationId); fact.Amount.Should().Be(10);
        await FluentActions.Awaiting(() => handler.Handle(request with { Amount = 9m }, default)).Should().ThrowAsync<BillingConflictException>();
        await FluentActions.Awaiting(() => handler.Handle(request with { Reason = CreditReason.BillingCorrection }, default)).Should().ThrowAsync<BillingConflictException>();
        await FluentActions.Awaiting(() => new IssueCreditNoteHandler(f.Store, new Actor("other-admin", true), f.Endpoint, new Clock()).Handle(request, default)).Should().ThrowAsync<BillingConflictException>();
    }
    [Fact]
    public async Task Multiple_credits_are_bounded_and_do_not_emit_additional_payment_events()
    {
        var f = Fixture(); f.Invoice.RecordPayment(MockPaymentOutcome.Approved);
        var handler = new IssueCreditNoteHandler(f.Store, new Actor("admin", true), f.Endpoint, new Clock());
        await handler.Handle(new(f.Invoice.Id, Guid.NewGuid(), 7m, CreditReason.BillingCorrection), default);
        await FluentActions.Awaiting(() => handler.Handle(new(f.Invoice.Id, Guid.NewGuid(), 3.01m, CreditReason.ServiceIssue), default)).Should().ThrowAsync<BillingConflictException>();
        await handler.Handle(new(f.Invoice.Id, Guid.NewGuid(), 3m, CreditReason.ServiceIssue), default);
        f.Invoice.CreditTotal.Should().Be(10m); f.Publisher.Events.Should().HaveCount(2).And.OnlyContain(x => x is CreditNoteIssued);
    }
    [Fact]
    public async Task Customer_cannot_credit_even_own_invoice_or_replay_admin_operation()
    {
        var f = Fixture(); f.Invoice.RecordPayment(MockPaymentOutcome.Approved);
        await FluentActions.Awaiting(() => new IssueCreditNoteHandler(f.Store, new Actor("alice"), f.Endpoint, new Clock()).Handle(new(f.Invoice.Id, Guid.NewGuid(), 1m, CreditReason.ServiceIssue), default)).Should().ThrowAsync<BillingForbiddenException>();
        f.Store.Locked.Should().BeEmpty(); f.Store.Saves.Should().Be(0);
    }
    [Fact]
    public async Task Non_owner_is_forbidden_before_payment_or_credit_metadata_is_read()
    {
        var f = Fixture(); var actor = new Actor("mallory");
        await FluentActions.Awaiting(() => new PayInvoiceHandler(f.Store, actor, f.Endpoint, new Clock()).Handle(new(f.Invoice.Id, Guid.NewGuid(), MockPaymentOutcome.Approved), default)).Should().ThrowAsync<BillingForbiddenException>();
        await FluentActions.Awaiting(() => new GetInvoiceHandler(f.Store, actor).Handle(new(f.Invoice.Id), default)).Should().ThrowAsync<BillingForbiddenException>();
        await FluentActions.Awaiting(() => new GetInvoicePaymentHandler(f.Store, actor).Handle(new(f.Invoice.Id), default)).Should().ThrowAsync<BillingForbiddenException>();
        await FluentActions.Awaiting(() => new ListInvoiceCreditNotesHandler(f.Store, actor).Handle(new(f.Invoice.Id), default)).Should().ThrowAsync<BillingForbiddenException>();
        await FluentActions.Awaiting(() => new GetCreditNoteHandler(f.Store, actor).Handle(new(f.Invoice.Id, Guid.NewGuid()), default)).Should().ThrowAsync<BillingForbiddenException>();
        f.Store.PaymentReads.Should().Be(0); f.Store.CreditReads.Should().Be(0); f.Store.Saves.Should().Be(0);
    }
    [Fact]
    public async Task List_is_scoped_to_subject_unless_admin_and_never_accepts_blank_subject()
    {
        var f = Fixture(); f.Store.Invoices.Add(new(Guid.NewGuid(), "bob", 10, "EUR", Issued));
        (await new ListInvoicesHandler(f.Store, new Actor("alice")).Handle(new(), default)).Total.Should().Be(1);
        (await new ListInvoicesHandler(f.Store, new Actor("admin", true)).Handle(new(), default)).Total.Should().Be(2);
        await FluentActions.Awaiting(() => new ListInvoicesHandler(f.Store, new Actor("", true)).Handle(new(), default)).Should().ThrowAsync<BillingForbiddenException>();
    }
    [Fact]
    public async Task Invoice_scoped_credit_lookup_does_not_expose_another_invoice_credit()
    {
        var f = Fixture(); f.Store.Credits.Add(new(Guid.NewGuid(), Guid.NewGuid(), 1, "EUR", CreditReason.ServiceIssue, Issued, "admin"));
        await FluentActions.Awaiting(() => new GetCreditNoteHandler(f.Store, new Actor("alice")).Handle(new(f.Invoice.Id, f.Store.Credits.Single().Id), default)).Should().ThrowAsync<BillingNotFoundException>();
    }
    [Theory]
    [InlineData("register")]
    [InlineData("pay")]
    [InlineData("credit")]
    public async Task Publish_failure_prevents_saving_the_transaction(string command)
    {
        var f = Fixture(); f.Publisher.Failure = new InvalidOperationException("Outbox unavailable");
        Func<Task> action = command switch
        {
            "register" => () => new RegisterInvoiceHandler(f.Store, f.Endpoint).Handle(new(Guid.NewGuid(), "alice", 10, "EUR", Issued), default),
            "pay" => () => new PayInvoiceHandler(f.Store, new Actor("alice"), f.Endpoint, new Clock()).Handle(new(f.Invoice.Id, Guid.NewGuid(), MockPaymentOutcome.Approved), default),
            _ => () => new IssueCreditNoteHandler(f.Store, new Actor("admin", true), f.Endpoint, new Clock()).Handle(new(f.Invoice.Id, Guid.NewGuid(), 1, CreditReason.BillingCorrection), default)
        };
        if (command == "credit") f.Invoice.RecordPayment(MockPaymentOutcome.Approved);
        await FluentActions.Awaiting(action).Should().ThrowAsync<InvalidOperationException>(); f.Store.Saves.Should().Be(0);
    }
    [Fact]
    public void Validation_rejects_invalid_enums_timestamps_and_subjects()
    {
        new PayInvoiceValidator().Validate(new PayInvoice(Guid.NewGuid(), Guid.NewGuid(), (MockPaymentOutcome)99)).IsValid.Should().BeFalse();
        new IssueCreditNoteValidator().Validate(new IssueCreditNote(Guid.NewGuid(), Guid.NewGuid(), 1, (CreditReason)99)).IsValid.Should().BeFalse();
        new RegisterInvoiceValidator().Validate(new RegisterInvoice(Guid.NewGuid(), "alice", 1, "EUR", default)).IsValid.Should().BeFalse();
        new RegisterInvoiceValidator().Validate(new RegisterInvoice(Guid.NewGuid(), "alice\u0000", 1, "EUR", Issued)).IsValid.Should().BeFalse();
    }
    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 101)]
    [InlineData(1000001, 1)]
    public void Pagination_is_bounded(int page, int size)
    {
        new ListInvoicesValidator().Validate(new ListInvoices(page, size)).IsValid.Should().BeFalse();
        new ListInvoiceCreditNotesValidator().Validate(new ListInvoiceCreditNotes(Guid.NewGuid(), page, size)).IsValid.Should().BeFalse();
    }
    [Fact]
    public async Task Payment_and_credit_may_share_operation_identifier_but_publish_distinct_event_identifiers()
    {
        var f = Fixture(); var operation = Guid.NewGuid();
        await new PayInvoiceHandler(f.Store, new Actor("alice"), f.Endpoint, new Clock())
            .Handle(new(f.Invoice.Id, operation, MockPaymentOutcome.Approved), default);
        var handler = new IssueCreditNoteHandler(f.Store, new Actor("admin", true), f.Endpoint, new Clock());
        var request = new IssueCreditNote(f.Invoice.Id, operation, 1m, CreditReason.BillingCorrection);
        var credit = await handler.Handle(request, default);
        (await handler.Handle(request, default)).Should().Be(credit);
        var paymentEvent = f.Publisher.Events.OfType<PaymentCompleted>().Single();
        var creditEvent = f.Publisher.Events.OfType<CreditNoteIssued>().Single();
        paymentEvent.EventId.Should().Be(operation);
        creditEvent.EventId.Should().NotBe(paymentEvent.EventId).And.NotBeEmpty();
        creditEvent.CreditNoteId.Should().Be(operation); credit.Id.Should().Be(operation);
        f.Store.Saves.Should().Be(2); f.Publisher.Events.Should().HaveCount(2);
    }
    private static Invoice Invoice(decimal amount = 10m) => new(Guid.NewGuid(), "alice", amount, "EUR", Issued);
    private static F Fixture(bool add = true)
    {
        var store = new Store(); var invoice = Invoice(); if (add) store.Invoices.Add(invoice);
        var endpoint = DispatchProxy.Create<IPublishEndpoint, Publisher>(); return new(store, invoice, endpoint, (Publisher)endpoint);
    }
    private sealed record F(Store Store, Invoice Invoice, IPublishEndpoint Endpoint, Publisher Publisher);
    private sealed record Actor(string Subject, bool IsAdmin = false) : IBillingActor;
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Issued.AddHours(1).AddTicks(7); }
    public class Publisher : DispatchProxy
    {
        public List<object> Events { get; } = []; public Exception? Failure { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) { if (Failure is not null) return Task.FromException(Failure); Events.Add(args![0]!); return Task.CompletedTask; }
    }
    private sealed class Store : IBillingStore
    {
        public List<Invoice> Invoices { get; } = []; public List<PaymentAttempt> Payments { get; } = []; public List<CreditNote> Credits { get; } = [];
        public List<Guid> Locked { get; } = []; public int Saves { get; private set; } public int PaymentReads { get; private set; } public int CreditReads { get; private set; }
        public Task LockInvoiceAsync(Guid id, CancellationToken ct) { Locked.Add(id); return Task.CompletedTask; }
        public Task<Invoice?> FindInvoiceAsync(Guid id, CancellationToken ct) => Task.FromResult(Invoices.SingleOrDefault(x => x.Id == id));
        public Task<PaymentAttempt?> FindPaymentByOperationAsync(Guid id, CancellationToken ct) { PaymentReads++; return Task.FromResult(Payments.SingleOrDefault(x => x.OperationId == id)); }
        public Task<PaymentAttempt?> FindInvoicePaymentAsync(Guid id, CancellationToken ct) { PaymentReads++; return Task.FromResult(Payments.SingleOrDefault(x => x.InvoiceId == id)); }
        public Task<CreditNote?> FindCreditNoteAsync(Guid id, CancellationToken ct) { CreditReads++; return Task.FromResult(Credits.SingleOrDefault(x => x.Id == id)); }
        public void AddInvoice(Invoice value) => Invoices.Add(value); public void AddPayment(PaymentAttempt value) => Payments.Add(value); public void AddCreditNote(CreditNote value) => Credits.Add(value);
        public Task SaveChangesAsync(CancellationToken ct) { Saves++; return Task.CompletedTask; }
        public Task<InvoicePageDto> ListInvoicesAsync(string? ownerSubject, int page, int pageSize, CancellationToken ct)
        {
            var items = Invoices.Where(x => ownerSubject is null || x.CustomerSubject == ownerSubject).ToArray();
            return Task.FromResult(new InvoicePageDto(page, pageSize, items.Length, items.Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.ToDto()).ToArray()));
        }
        public Task<CreditNotePageDto> ListCreditNotesAsync(Guid id, int page, int pageSize, CancellationToken ct)
        {
            var items = Credits.Where(x => x.InvoiceId == id).ToArray();
            return Task.FromResult(new CreditNotePageDto(page, pageSize, items.Length, items.Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.ToDto()).ToArray()));
        }
    }
}
