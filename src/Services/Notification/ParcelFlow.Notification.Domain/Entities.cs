using System.Net.Mail;
using System.Text.RegularExpressions;

namespace ParcelFlow.Notification.Domain;

public enum NotificationKind { ShipmentCreated, TrackingStatus, InvoiceIssued, PaymentCompleted, PaymentFailed, CreditNoteIssued }
public enum ParcelStatus { Created, PickedUp, InTransit, AtSortingCenter, OutForDelivery, Delivered, Failed, Returned }
public enum NotificationChannel { Email, Sms }

public static class NotificationRules
{
    public static bool IsSubject(string? value) => IsText(value, 200) && value == value!.Trim();
    public static bool IsText(string? value, int maximumLength) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength && !value.Any(char.IsControl);
    public static bool IsTrackingNumber(string? value) => value is not null && value.Length == 26 && Regex.IsMatch(value, "^PF[A-F0-9]{24}$", RegexOptions.CultureInvariant);
    public static bool IsEmail(string? value) => IsText(value, 254) && MailAddress.TryCreate(value, out var address) && address.Address == value;
    public static bool IsPhone(string? value) => IsText(value, 30);
    public static bool IsAmount(decimal? value) => value is > 0 and <= 1_000_000m && decimal.Round(value.Value, 2) == value;
    public static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        if (value == default) throw new ArgumentException("A timestamp is required.", nameof(value));
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }
    public static bool IsPayload(NotificationKind kind, string? customerSubject, string? trackingNumber, ParcelStatus? status,
        Guid? invoiceId, Guid? creditNoteId, decimal? amount, string? currency)
    {
        if (!Enum.IsDefined(kind) || (customerSubject is not null && !IsSubject(customerSubject)) ||
            (trackingNumber is not null && !IsTrackingNumber(trackingNumber)) || (status.HasValue && !Enum.IsDefined(status.Value))) return false;
        return kind switch
        {
            NotificationKind.ShipmentCreated => customerSubject is not null && trackingNumber is not null && status is null && invoiceId is null && creditNoteId is null && amount is null && currency is null,
            NotificationKind.TrackingStatus => trackingNumber is not null && status.HasValue && invoiceId is null && creditNoteId is null && amount is null && currency is null,
            NotificationKind.InvoiceIssued => customerSubject is not null && invoiceId.HasValue && invoiceId != Guid.Empty && creditNoteId is null && status is null && IsAmount(amount) && currency == "EUR",
            NotificationKind.PaymentCompleted => status is null && invoiceId is null && creditNoteId is null && IsAmount(amount) && currency == "EUR",
            NotificationKind.PaymentFailed => status is null && invoiceId is null && creditNoteId is null && amount is null && currency is null,
            NotificationKind.CreditNoteIssued => customerSubject is not null && invoiceId.HasValue && invoiceId != Guid.Empty && creditNoteId.HasValue && creditNoteId != Guid.Empty && status is null && IsAmount(amount) && currency == "EUR",
            _ => false
        };
    }
}

public sealed class NotificationEvent
{
    private readonly List<NotificationMessage> _messages = [];
    private NotificationEvent() { }
    public NotificationEvent(Guid eventId, Guid shipmentId, NotificationKind kind, DateTimeOffset occurredAtUtc,
        string? customerSubject, string? trackingNumber, ParcelStatus? status, Guid? invoiceId, Guid? creditNoteId, decimal? amount, string? currency)
    {
        if (eventId == Guid.Empty || shipmentId == Guid.Empty) throw new ArgumentException("Valid event and shipment identifiers are required.");
        if (!NotificationRules.IsPayload(kind, customerSubject, trackingNumber, status, invoiceId, creditNoteId, amount, currency)) throw new ArgumentException("The notification metadata does not match its kind.");
        if (invoiceId.HasValue && invoiceId != shipmentId) throw new ArgumentException("The invoice must identify the shipment.");
        EventId = eventId; ShipmentId = shipmentId; Kind = kind; OccurredAtUtc = NotificationRules.NormalizeTimestamp(occurredAtUtc);
        CustomerSubject = customerSubject; TrackingNumber = trackingNumber; Status = status; InvoiceId = invoiceId;
        CreditNoteId = creditNoteId; Amount = amount; Currency = currency;
    }
    public Guid EventId { get; private set; }
    public Guid ShipmentId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string? CustomerSubject { get; private set; }
    public string? TrackingNumber { get; private set; }
    public ParcelStatus? Status { get; private set; }
    public Guid? InvoiceId { get; private set; }
    public Guid? CreditNoteId { get; private set; }
    public decimal? Amount { get; private set; }
    public string? Currency { get; private set; }
    public IReadOnlyCollection<NotificationMessage> Messages => _messages.AsReadOnly();
    public bool Matches(Guid shipmentId, NotificationKind kind, DateTimeOffset occurredAtUtc, string? customerSubject,
        string? trackingNumber, ParcelStatus? status, Guid? invoiceId, Guid? creditNoteId, decimal? amount, string? currency) =>
        ShipmentId == shipmentId && Kind == kind && OccurredAtUtc == NotificationRules.NormalizeTimestamp(occurredAtUtc) &&
        CustomerSubject == customerSubject && TrackingNumber == trackingNumber && Status == status && InvoiceId == invoiceId &&
        CreditNoteId == creditNoteId && Amount == amount && Currency == currency;
    public void RecordMessages(string customerSubject, string trackingNumber, string email, string phone, string subject,
        string emailBody, string smsBody, DateTimeOffset recordedAtUtc)
    {
        if (_messages.Count != 0) throw new InvalidOperationException("An event may be notified only once per channel.");
        if (!NotificationRules.IsSubject(customerSubject) || !NotificationRules.IsTrackingNumber(trackingNumber) ||
            !NotificationRules.IsEmail(email) || !NotificationRules.IsPhone(phone)) throw new ArgumentException("Valid shipment recipient details are required.");
        if (CustomerSubject is not null && CustomerSubject != customerSubject || TrackingNumber is not null && TrackingNumber != trackingNumber)
            throw new InvalidOperationException("Recipient details conflict with the original shipment.");
        if (!NotificationRules.IsText(subject, 200) || !NotificationRules.IsText(emailBody, 2000) || !NotificationRules.IsText(smsBody, 320))
            throw new ArgumentException("Notification content exceeds the channel limits or contains control characters.");
        var recorded = NotificationRules.NormalizeTimestamp(recordedAtUtc);
        _messages.Add(new NotificationMessage(Guid.NewGuid(), EventId, ShipmentId, customerSubject, trackingNumber,
            NotificationChannel.Email, email, subject, emailBody, OccurredAtUtc, recorded));
        _messages.Add(new NotificationMessage(Guid.NewGuid(), EventId, ShipmentId, customerSubject, trackingNumber,
            NotificationChannel.Sms, phone, subject, smsBody, OccurredAtUtc, recorded));
    }
}

public sealed class NotificationMessage
{
    private NotificationMessage() { }
    internal NotificationMessage(Guid id, Guid sourceEventId, Guid shipmentId, string customerSubject, string trackingNumber,
        NotificationChannel channel, string destination, string subject, string body, DateTimeOffset occurredAtUtc, DateTimeOffset recordedAtUtc)
    {
        Id = id; SourceEventId = sourceEventId; ShipmentId = shipmentId; CustomerSubject = customerSubject; TrackingNumber = trackingNumber;
        Channel = channel; Destination = destination; Subject = subject; Body = body; OccurredAtUtc = occurredAtUtc; RecordedAtUtc = recordedAtUtc;
    }
    public Guid Id { get; private set; }
    public Guid SourceEventId { get; private set; }
    public Guid ShipmentId { get; private set; }
    public string CustomerSubject { get; private set; } = string.Empty;
    public string TrackingNumber { get; private set; } = string.Empty;
    public NotificationChannel Channel { get; private set; }
    public string Destination { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset RecordedAtUtc { get; private set; }
    public string DeliveryStatus => "MockSent";
}
