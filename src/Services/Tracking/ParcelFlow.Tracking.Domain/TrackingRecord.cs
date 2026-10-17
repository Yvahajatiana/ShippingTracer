using System.Text.RegularExpressions;

namespace ParcelFlow.Tracking.Domain;

public enum ParcelStatus
{
    Created = 0, PickedUp = 1, InTransit = 2, AtSortingCenter = 3,
    OutForDelivery = 4, Delivered = 5, Failed = 6, Returned = 7
}

public static class TrackingRules
{
    // PostgreSQL timestamptz persists microseconds; use the same precision before deduplication.
    public static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }
    public static string NormalizeTrackingNumber(string value) => value.Trim().ToUpperInvariant();
    public static bool IsTrackingNumber(string? value) => value is not null &&
        Regex.IsMatch(NormalizeTrackingNumber(value), "\\APF[A-F0-9]{24}\\z", RegexOptions.CultureInvariant);
    public static bool IsFacilityCode(string? value) => value is null ||
        Regex.IsMatch(value, "\\A[A-Z0-9][A-Z0-9-]{0,39}\\z", RegexOptions.CultureInvariant);
    public static string Label(this ParcelStatus status) => status switch
    {
        ParcelStatus.Created => "Créé",
        ParcelStatus.PickedUp => "Pris en charge",
        ParcelStatus.InTransit => "En transit",
        ParcelStatus.AtSortingCenter => "En centre de tri",
        ParcelStatus.OutForDelivery => "En livraison",
        ParcelStatus.Delivered => "Livré",
        ParcelStatus.Failed => "Échec",
        ParcelStatus.Returned => "Retour",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
    public static int TimelineRank(this ParcelStatus status) => status switch
    {
        ParcelStatus.Failed => 5,
        ParcelStatus.Delivered => 6,
        _ => (int)status
    };
}

public sealed class TrackingEntry
{
    private TrackingEntry() { }
    public TrackingEntry(Guid eventId, ParcelStatus status, DateTimeOffset occurredAtUtc, string? facilityCode)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("Event ID is required.", nameof(eventId));
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (occurredAtUtc == default) throw new ArgumentException("Occurrence time is required.", nameof(occurredAtUtc));
        if (!TrackingRules.IsFacilityCode(facilityCode)) throw new ArgumentException("Invalid facility code.", nameof(facilityCode));
        EventId = eventId;
        Status = status;
        OccurredAtUtc = TrackingRules.NormalizeTimestamp(occurredAtUtc);
        FacilityCode = facilityCode;
    }
    public Guid EventId { get; private set; }
    public ParcelStatus Status { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string? FacilityCode { get; private set; }
}

public sealed class TrackingRecord
{
    private readonly List<TrackingEntry> _entries = [];
    private TrackingRecord() { }
    public TrackingRecord(Guid shipmentId)
    {
        if (shipmentId == Guid.Empty) throw new ArgumentException("Shipment ID is required.", nameof(shipmentId));
        ShipmentId = shipmentId;
    }
    public Guid ShipmentId { get; private set; }
    public string? TrackingNumber { get; private set; }
    public long Revision { get; private set; }
    public IReadOnlyCollection<TrackingEntry> Entries => _entries.AsReadOnly();
    public IReadOnlyList<TrackingEntry> Timeline => _entries.OrderBy(x => x.OccurredAtUtc)
        .ThenBy(x => x.Status.TimelineRank()).ThenBy(x => x.EventId).ToArray();
    public ParcelStatus CurrentStatus => CurrentEntry.Status;
    public DateTimeOffset UpdatedAtUtc => CurrentEntry.OccurredAtUtc;
    private TrackingEntry CurrentEntry
    {
        get
        {
            TrackingEntry? current = null;
            foreach (var entry in Timeline)
            {
                if (current is null || (entry.Status != ParcelStatus.Created && current.Status != ParcelStatus.Returned &&
                    (current.Status != ParcelStatus.Delivered || entry.Status == ParcelStatus.Returned))) current = entry;
            }
            return current ?? throw new InvalidOperationException("Tracking has no events.");
        }
    }
    public void Register(string trackingNumber)
    {
        if (!TrackingRules.IsTrackingNumber(trackingNumber)) throw new ArgumentException("Invalid tracking number.", nameof(trackingNumber));
        var normalized = TrackingRules.NormalizeTrackingNumber(trackingNumber);
        if (TrackingNumber is not null && TrackingNumber != normalized) throw new InvalidOperationException("A shipment tracking number cannot change.");
        if (TrackingNumber is null) { TrackingNumber = normalized; Revision++; }
    }
    public bool AddEntry(TrackingEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var existing = _entries.SingleOrDefault(x => x.EventId == entry.EventId);
        if (existing is not null)
        {
            if (existing.Status != entry.Status || existing.OccurredAtUtc != entry.OccurredAtUtc || existing.FacilityCode != entry.FacilityCode)
                throw new InvalidOperationException("An event ID cannot identify different tracking facts.");
            return false;
        }
        _entries.Add(entry);
        Revision++;
        return true;
    }
}
