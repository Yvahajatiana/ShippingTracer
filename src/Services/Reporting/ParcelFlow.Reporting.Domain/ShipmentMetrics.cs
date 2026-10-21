using System.Text.RegularExpressions;

namespace ParcelFlow.Reporting.Domain;

public enum ParcelStatus
{
    Created = 0, PickedUp = 1, InTransit = 2, AtSortingCenter = 3,
    OutForDelivery = 4, Delivered = 5, Failed = 6, Returned = 7
}

public static class ReportingRules
{
    public static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }
    public static bool IsTrackingNumber(string? value) => value is not null &&
        Regex.IsMatch(value, "\\APF[A-F0-9]{24}\\z", RegexOptions.CultureInvariant);
    public static bool IsFacilityCode(string? value) => value is null ||
        Regex.IsMatch(value, "\\A[A-Z0-9][A-Z0-9-]{0,39}\\z", RegexOptions.CultureInvariant);
    public static int TimelineRank(this ParcelStatus status) => status switch
    {
        ParcelStatus.Failed => 5, ParcelStatus.Delivered => 6, _ => (int)status
    };
}

public sealed class StatusFact
{
    private StatusFact() { }
    public StatusFact(Guid eventId, Guid sourceEventId, Guid shipmentId, ParcelStatus status,
        DateTimeOffset occurredAtUtc, string? facilityCode)
    {
        if (eventId == Guid.Empty || sourceEventId == Guid.Empty || shipmentId == Guid.Empty)
            throw new ArgumentException("Event, source event and shipment identifiers are required.");
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (occurredAtUtc == default) throw new ArgumentException("Occurrence time is required.", nameof(occurredAtUtc));
        if (!ReportingRules.IsFacilityCode(facilityCode)) throw new ArgumentException("Invalid facility code.", nameof(facilityCode));
        EventId = eventId; SourceEventId = sourceEventId; ShipmentId = shipmentId; Status = status;
        OccurredAtUtc = ReportingRules.NormalizeTimestamp(occurredAtUtc); FacilityCode = facilityCode;
    }
    public Guid EventId { get; private set; }
    public Guid SourceEventId { get; private set; }
    public Guid ShipmentId { get; private set; }
    public ParcelStatus Status { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string? FacilityCode { get; private set; }
    public bool Matches(Guid sourceEventId, Guid shipmentId, ParcelStatus status, DateTimeOffset occurredAtUtc, string? facilityCode) =>
        SourceEventId == sourceEventId && ShipmentId == shipmentId && Status == status &&
        OccurredAtUtc == ReportingRules.NormalizeTimestamp(occurredAtUtc) && FacilityCode == facilityCode;
}

public sealed class ShipmentMetrics
{
    private readonly List<StatusFact> _facts = [];
    private ShipmentMetrics() { }
    public ShipmentMetrics(Guid shipmentId, string trackingNumber)
    {
        if (shipmentId == Guid.Empty) throw new ArgumentException("Shipment identifier is required.", nameof(shipmentId));
        if (!ReportingRules.IsTrackingNumber(trackingNumber)) throw new ArgumentException("Invalid tracking number.", nameof(trackingNumber));
        ShipmentId = shipmentId; TrackingNumber = trackingNumber;
    }
    public Guid ShipmentId { get; private set; }
    public string TrackingNumber { get; private set; } = "";
    public long Revision { get; private set; }
    public IReadOnlyCollection<StatusFact> Facts => _facts.AsReadOnly();
    public DateTimeOffset? CreatedAtUtc { get; private set; }
    public DateTimeOffset? PickedUpAtUtc { get; private set; }
    public DateTimeOffset? DeliveredAtUtc { get; private set; }
    public ParcelStatus CurrentStatus { get; private set; }
    private ParcelStatus FoldStatus
    {
        get
        {
            StatusFact? current = null;
            foreach (var fact in _facts.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Status.TimelineRank()).ThenBy(x => x.SourceEventId))
                if (current is null || (fact.Status != ParcelStatus.Created && current.Status != ParcelStatus.Returned &&
                    (current.Status != ParcelStatus.Delivered || fact.Status == ParcelStatus.Returned))) current = fact;
            return current?.Status ?? throw new InvalidOperationException("Shipment has no tracking facts.");
        }
    }
    public decimal? DeliverySeconds { get; private set; }
    public decimal? TransitSeconds { get; private set; }
    private DateTimeOffset? Earliest(ParcelStatus status) => _facts.Where(x => x.Status == status).Select(x => (DateTimeOffset?)x.OccurredAtUtc).Min();
    private decimal? Duration(DateTimeOffset? start) => CurrentStatus == ParcelStatus.Delivered && start is not null &&
        DeliveredAtUtc is { } end && end >= start.Value ? (end.Ticks - start.Value.Ticks) / (decimal)TimeSpan.TicksPerSecond : null;
    public bool AddFact(StatusFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact.ShipmentId != ShipmentId) throw new InvalidOperationException("The fact belongs to another shipment.");
        var sameEnvelope = _facts.SingleOrDefault(x => x.EventId == fact.EventId);
        if (sameEnvelope is not null && !sameEnvelope.Matches(fact.SourceEventId, fact.ShipmentId, fact.Status, fact.OccurredAtUtc, fact.FacilityCode))
            throw new InvalidOperationException("An event identifier cannot identify different facts.");
        var sameSource = _facts.SingleOrDefault(x => x.SourceEventId == fact.SourceEventId);
        if (sameSource is not null)
        {
            if (!sameSource.Matches(fact.SourceEventId, fact.ShipmentId, fact.Status, fact.OccurredAtUtc, fact.FacilityCode))
                throw new InvalidOperationException("A source identifier cannot identify different facts.");
            return false;
        }
        _facts.Add(fact); Revision++;
        CurrentStatus = FoldStatus;
        CreatedAtUtc = Earliest(ParcelStatus.Created); PickedUpAtUtc = Earliest(ParcelStatus.PickedUp); DeliveredAtUtc = Earliest(ParcelStatus.Delivered);
        DeliverySeconds = Duration(CreatedAtUtc); TransitSeconds = Duration(PickedUpAtUtc);
        return true;
    }
}

