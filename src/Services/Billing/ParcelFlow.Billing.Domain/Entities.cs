namespace ParcelFlow.Billing.Domain;

public enum InvoiceStatus { PendingPayment, Paid, PaymentFailed, PartiallyCredited, Credited }
public enum MockPaymentOutcome { Approved, Declined }
public enum CreditReason { BillingCorrection, ServiceIssue }

public static class BillingRules
{
    public static bool IsSubject(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim() && !value.Any(char.IsControl);
    public static bool IsAmount(decimal value) => value > 0 && value <= 1_000_000m && decimal.Round(value, 2) == value;
    public static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        if (value == default) throw new ArgumentException("A timestamp is required.", nameof(value));
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }
    public static void RequireId(Guid id) { if (id == Guid.Empty) throw new ArgumentException("An identifier is required.", nameof(id)); }
    public static void RequireMoney(decimal amount, string currency)
    {
        if (!IsAmount(amount)) throw new ArgumentException("Amount must be positive, at most 1000000, with at most two decimal places.", nameof(amount));
        if (currency != "EUR") throw new ArgumentException("Only EUR is supported.", nameof(currency));
    }
    public static void RequireSubject(string subject) { if (!IsSubject(subject)) throw new ArgumentException("A valid subject is required.", nameof(subject)); }
}

public sealed class Invoice
{
    private Invoice() { }
    public Invoice(Guid shipmentId, string customerSubject, decimal amount, string currency, DateTimeOffset issuedAtUtc)
    {
        BillingRules.RequireId(shipmentId); BillingRules.RequireSubject(customerSubject); BillingRules.RequireMoney(amount, currency);
        Id = shipmentId; CustomerSubject = customerSubject; Amount = amount; Currency = currency; IssuedAtUtc = BillingRules.NormalizeTimestamp(issuedAtUtc); LastActionAtUtc = IssuedAtUtc;
    }
    public Guid Id { get; private set; }
    public Guid ShipmentId => Id;
    public string Number => "INV-" + Id.ToString("N").ToUpperInvariant();
    public string CustomerSubject { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "EUR";
    public DateTimeOffset IssuedAtUtc { get; private set; }
    public DateTimeOffset LastActionAtUtc { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public decimal CreditTotal { get; private set; }
    public long Revision { get; private set; }
    public decimal RemainingCredit => Status is InvoiceStatus.Paid or InvoiceStatus.PartiallyCredited or InvoiceStatus.Credited ? Amount - CreditTotal : 0m;
    public bool Matches(string customerSubject, decimal amount, string currency, DateTimeOffset issuedAtUtc) => CustomerSubject == customerSubject && Amount == amount && Currency == currency && IssuedAtUtc == BillingRules.NormalizeTimestamp(issuedAtUtc);
    public DateTimeOffset RecordAction(DateTimeOffset now)
    {
        var normalized = BillingRules.NormalizeTimestamp(now);
        LastActionAtUtc = normalized > LastActionAtUtc ? normalized : LastActionAtUtc.AddTicks(10);
        return LastActionAtUtc;
    }
    public void RecordPayment(MockPaymentOutcome outcome)
    {
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        if (Status != InvoiceStatus.PendingPayment) throw new InvalidOperationException("An invoice accepts only one terminal payment attempt.");
        Status = outcome == MockPaymentOutcome.Approved ? InvoiceStatus.Paid : InvoiceStatus.PaymentFailed;
        Revision++;
    }
    public void IssueCredit(decimal amount)
    {
        if (!BillingRules.IsAmount(amount)) throw new ArgumentException("Invalid credit amount.", nameof(amount));
        if (Status is not (InvoiceStatus.Paid or InvoiceStatus.PartiallyCredited)) throw new InvalidOperationException("Only a paid invoice with remaining credit can be credited.");
        if (amount > RemainingCredit) throw new InvalidOperationException("Credit exceeds the amount remaining on this invoice.");
        CreditTotal += amount; Status = CreditTotal == Amount ? InvoiceStatus.Credited : InvoiceStatus.PartiallyCredited; Revision++;
    }
}

public sealed class PaymentAttempt
{
    private PaymentAttempt() { }
    public PaymentAttempt(Guid operationId, Guid invoiceId, MockPaymentOutcome outcome, decimal amount, string currency, DateTimeOffset occurredAtUtc, string actorSubject)
    {
        BillingRules.RequireId(operationId); BillingRules.RequireId(invoiceId); BillingRules.RequireMoney(amount, currency); BillingRules.RequireSubject(actorSubject);
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        OperationId = operationId; InvoiceId = invoiceId; Outcome = outcome; Amount = amount; Currency = currency; OccurredAtUtc = BillingRules.NormalizeTimestamp(occurredAtUtc); ActorSubject = actorSubject;
    }
    public Guid OperationId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public MockPaymentOutcome Outcome { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "EUR";
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string ActorSubject { get; private set; } = string.Empty;
    public bool Matches(Guid invoiceId, MockPaymentOutcome outcome, string actorSubject) => InvoiceId == invoiceId && Outcome == outcome && ActorSubject == actorSubject;
}

public sealed class CreditNote
{
    private CreditNote() { }
    public CreditNote(Guid id, Guid invoiceId, decimal amount, string currency, CreditReason reason, DateTimeOffset occurredAtUtc, string actorSubject)
    {
        BillingRules.RequireId(id); BillingRules.RequireId(invoiceId); BillingRules.RequireMoney(amount, currency); BillingRules.RequireSubject(actorSubject);
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        Id = id; InvoiceId = invoiceId; Amount = amount; Currency = currency; Reason = reason; OccurredAtUtc = BillingRules.NormalizeTimestamp(occurredAtUtc); ActorSubject = actorSubject;
    }
    public Guid Id { get; private set; }
    public string Number => "CRN-" + Id.ToString("N").ToUpperInvariant();
    public Guid InvoiceId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "EUR";
    public CreditReason Reason { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string ActorSubject { get; private set; } = string.Empty;
    public bool Matches(Guid invoiceId, decimal amount, CreditReason reason, string actorSubject) => InvoiceId == invoiceId && Amount == amount && Reason == reason && ActorSubject == actorSubject;
}
