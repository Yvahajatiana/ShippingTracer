namespace ParcelFlow.Delivery.Domain;

public enum DeliveryRouteStatus { Planned, InProgress, Completed }
public enum DeliveryStopStatus { Pending, Delivered, Failed }
public enum DeliveryOutcome { Delivered, Failed }
public enum DeliveryFailureReason { RecipientAbsent, AddressNotFound, RecipientRefused }
public static class DeliveryRules
{
    public static string NormalizeTrackingNumber(string value) => value.Trim().ToUpperInvariant();
    public static bool IsTrackingNumber(string? value) => value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, "^PF[0-9A-F]{24}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    public static bool IsText(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximum && !value.Any(char.IsControl);
    public static DateTimeOffset NormalizeTimestamp(DateTimeOffset value) => new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
}
public sealed class DeliveryDriver
{
    private DeliveryDriver() { }
    public DeliveryDriver(string subject, string displayName)
    {
        if (!DeliveryRules.IsText(subject, 200) || !DeliveryRules.IsText(displayName, 120)) throw new ArgumentException("Invalid driver identity.");
        Subject = subject.Trim(); DisplayName = displayName.Trim(); IsActive = true;
    }
    public string Subject { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public void Update(string displayName, bool isActive)
    {
        if (!DeliveryRules.IsText(displayName, 120)) throw new ArgumentException("Invalid driver name.");
        DisplayName = displayName.Trim(); IsActive = isActive;
    }
}
public sealed class DeliveryParcel
{
    private DeliveryParcel() { }
    public DeliveryParcel(Guid shipmentId) { if (shipmentId == Guid.Empty) throw new ArgumentException("Shipment is required."); ShipmentId = shipmentId; }
    public Guid ShipmentId { get; private set; }
    public string? TrackingNumber { get; private set; }
    public bool IsPickedUp { get; private set; }
    public bool IsDelivered { get; private set; }
    public Guid? ActiveRouteId { get; private set; }
    public DateTimeOffset? LastDeliveryEventAtUtc { get; private set; }
    public long Revision { get; private set; }
    public bool Register(string trackingNumber)
    {
        var number = DeliveryRules.NormalizeTrackingNumber(trackingNumber);
        if (!DeliveryRules.IsTrackingNumber(number)) throw new ArgumentException("Invalid tracking number.");
        if (TrackingNumber is not null && TrackingNumber != number) throw new InvalidOperationException("Shipment has another tracking number.");
        if (TrackingNumber is not null) return false;
        TrackingNumber = number; Revision++; return true;
    }
    public bool MarkPickedUp() { if (IsPickedUp) return false; IsPickedUp = true; Revision++; return true; }
    public void Assign(Guid routeId)
    {
        if (routeId == Guid.Empty || TrackingNumber is null || !IsPickedUp || IsDelivered || ActiveRouteId is not null) throw new InvalidOperationException("Parcel is not available for this route.");
        ActiveRouteId = routeId; Revision++;
    }
    public void Release(Guid routeId)
    {
        if (ActiveRouteId != routeId) throw new InvalidOperationException("Parcel does not belong to this route.");
        ActiveRouteId = null; Revision++;
    }
    public DateTimeOffset RecordEvent(DateTimeOffset now)
    {
        now = DeliveryRules.NormalizeTimestamp(now);
        if (LastDeliveryEventAtUtc is { } previous && now <= previous) now = previous.AddTicks(10);
        LastDeliveryEventAtUtc = now; Revision++; return now;
    }
    public void Complete(Guid routeId, bool delivered)
    {
        Release(routeId); if (delivered) IsDelivered = true;
    }
}
public sealed class DeliveryStop
{
    private DeliveryStop() { }
    internal DeliveryStop(Guid routeId, Guid shipmentId, int sequence) { RouteId = routeId; ShipmentId = shipmentId; Sequence = sequence; }
    public Guid RouteId { get; private set; }
    public Guid ShipmentId { get; private set; }
    public int Sequence { get; private set; }
    public DeliveryStopStatus Status { get; private set; }
    public DeliveryFailureReason? FailureReason { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    internal void Complete(DeliveryOutcome outcome, DeliveryFailureReason? reason, DateTimeOffset at)
    {
        if (!Enum.IsDefined(outcome) || (outcome == DeliveryOutcome.Delivered && reason is not null) || (outcome == DeliveryOutcome.Failed && (reason is null || !Enum.IsDefined(reason.Value)))) throw new ArgumentException("Invalid stop outcome.");
        if (Status != DeliveryStopStatus.Pending) throw new InvalidOperationException("Stop already has an outcome.");
        Status = outcome == DeliveryOutcome.Delivered ? DeliveryStopStatus.Delivered : DeliveryStopStatus.Failed;
        FailureReason = reason; CompletedAtUtc = DeliveryRules.NormalizeTimestamp(at);
    }
}
public sealed class DeliveryRoute
{
    private readonly List<DeliveryStop> _stops = [];
    private DeliveryRoute() { }
    public DeliveryRoute(Guid id, string name, DateOnly serviceDate)
    {
        if (id == Guid.Empty || !DeliveryRules.IsText(name, 120) || serviceDate == default) throw new ArgumentException("Invalid route.");
        Id = id; Name = name.Trim(); ServiceDate = serviceDate;
    }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateOnly ServiceDate { get; private set; }
    public string? DriverSubject { get; private set; }
    public DeliveryRouteStatus Status { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public long Revision { get; private set; }
    public IReadOnlyCollection<DeliveryStop> Stops => _stops.AsReadOnly();
    public void EnsurePlanned() { if (Status != DeliveryRouteStatus.Planned) throw new InvalidOperationException("Only planned routes can be edited."); }
    public void AssignDriver(DeliveryDriver driver) { EnsurePlanned(); if (!driver.IsActive) throw new InvalidOperationException("Driver is inactive."); DriverSubject = driver.Subject; Revision++; }
    public void AddParcel(DeliveryParcel parcel)
    {
        EnsurePlanned(); if (_stops.Any(x => x.ShipmentId == parcel.ShipmentId)) throw new InvalidOperationException("Parcel is already on the route.");
        parcel.Assign(Id); _stops.Add(new(Id, parcel.ShipmentId, _stops.Count == 0 ? 1 : _stops.Max(x => x.Sequence) + 1)); Revision++;
    }
    public void RemoveParcel(DeliveryParcel parcel)
    {
        EnsurePlanned(); var stop = _stops.SingleOrDefault(x => x.ShipmentId == parcel.ShipmentId) ?? throw new InvalidOperationException("Parcel is not on this route.");
        parcel.Release(Id); _stops.Remove(stop); Revision++;
    }
    public void Start(DeliveryDriver driver, DateTimeOffset at)
    {
        EnsurePlanned(); if (_stops.Count == 0 || !driver.IsActive || DriverSubject != driver.Subject) throw new InvalidOperationException("An active driver and at least one parcel are required.");
        Status = DeliveryRouteStatus.InProgress; StartedAtUtc = DeliveryRules.NormalizeTimestamp(at); Revision++;
    }
    public void CompleteStop(Guid shipmentId, DeliveryOutcome outcome, DeliveryFailureReason? reason, DateTimeOffset at)
    {
        if (Status != DeliveryRouteStatus.InProgress) throw new InvalidOperationException("Route has not started or is already completed.");
        var stop = _stops.SingleOrDefault(x => x.ShipmentId == shipmentId) ?? throw new InvalidOperationException("Parcel is not on this route.");
        stop.Complete(outcome, reason, at); Revision++;
        if (_stops.All(x => x.Status != DeliveryStopStatus.Pending)) { Status = DeliveryRouteStatus.Completed; CompletedAtUtc = _stops.Max(x => x.CompletedAtUtc); }
    }
}
public sealed class DeliveryAttempt
{
    private DeliveryAttempt() { }
    public DeliveryAttempt(Guid operationId, Guid routeId, Guid shipmentId, DeliveryOutcome outcome, DateTimeOffset occurredAtUtc, string actorSubject, string? recipientName, DeliveryFailureReason? failureReason, byte[]? signaturePng, byte[]? photo, string? photoContentType)
    {
        if (operationId == Guid.Empty || routeId == Guid.Empty || shipmentId == Guid.Empty || !DeliveryRules.IsText(actorSubject, 200) || !Enum.IsDefined(outcome)) throw new ArgumentException("Invalid delivery attempt.");
        if (outcome == DeliveryOutcome.Delivered && (!DeliveryRules.IsText(recipientName, 120) || failureReason is not null || (signaturePng is null && photo is null))) throw new ArgumentException("Delivery proof and recipient are required.");
        if (outcome == DeliveryOutcome.Failed && (failureReason is null || !Enum.IsDefined(failureReason.Value) || recipientName is not null || signaturePng is not null || photo is not null)) throw new ArgumentException("Invalid failure details.");
        OperationId = operationId; RouteId = routeId; ShipmentId = shipmentId; Outcome = outcome; OccurredAtUtc = DeliveryRules.NormalizeTimestamp(occurredAtUtc); ActorSubject = actorSubject; RecipientName = recipientName?.Trim(); FailureReason = failureReason; SignaturePng = signaturePng?.ToArray(); Photo = photo?.ToArray(); PhotoContentType = photoContentType;
    }
    public Guid OperationId { get; private set; }
    public Guid RouteId { get; private set; }
    public Guid ShipmentId { get; private set; }
    public DeliveryOutcome Outcome { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string ActorSubject { get; private set; } = string.Empty;
    public string? RecipientName { get; private set; }
    public DeliveryFailureReason? FailureReason { get; private set; }
    public byte[]? SignaturePng { get; private set; }
    public byte[]? Photo { get; private set; }
    public string? PhotoContentType { get; private set; }
    public bool Matches(Guid routeId, Guid shipmentId, DeliveryOutcome outcome, string actor, string? recipient, DeliveryFailureReason? reason, byte[]? signature, byte[]? photo) => RouteId == routeId && ShipmentId == shipmentId && Outcome == outcome && ActorSubject == actor && RecipientName == recipient?.Trim() && FailureReason == reason && BytesEqual(SignaturePng, signature) && BytesEqual(Photo, photo);
    private static bool BytesEqual(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
}
