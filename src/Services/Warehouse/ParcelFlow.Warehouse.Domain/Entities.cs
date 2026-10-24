using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ParcelFlow.Warehouse.Domain;

public enum WarehouseKind { Warehouse = 0, SortingCenter = 1 }
public enum ScanDirection { Entry = 0, Exit = 1 }

public static class WarehouseRules
{
    public static string NormalizeTrackingNumber(string value) => value.Trim().ToUpperInvariant();
    public static bool IsTrackingNumber(string? value) => value is not null && Regex.IsMatch(value.Trim(), "^PF[0-9A-F]{24}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static bool IsCode(string? value) => value is not null && Regex.IsMatch(value.Trim().ToUpperInvariant(), "^[A-Z0-9][A-Z0-9-]{0,39}$", RegexOptions.CultureInvariant);
    public static bool IsCountryCode(string? value) => value is not null && Regex.IsMatch(value.Trim(), "^[A-Za-z]{2}$", RegexOptions.CultureInvariant);
    public static DateTimeOffset NormalizeTimestamp(DateTimeOffset value) => new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
    public static bool ContainsNoControlCharacters(string? value) => value is null || !value.Any(char.IsControl);
    /// <summary>Audit subject recorded on exits triggered automatically by the start of a delivery.</summary>
    public const string DeliveryStartOperator = "system:delivery-start";
    /// <summary>Derives a stable scan identifier from the integration event that triggered an automatic exit.</summary>
    public static Guid DeliveryExitScanId(Guid sourceEventId)
    {
        if (sourceEventId == Guid.Empty) throw new ArgumentException("Source event ID is required.", nameof(sourceEventId));
        Span<byte> input = stackalloc byte[40];
        "warehouse:delivery-exit:"u8.CopyTo(input);
        sourceEventId.TryWriteBytes(input[24..40]);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        var bytes = hash[..16];
        // Mark the value as an RFC 9562 version 8 (custom) UUID with the RFC variant.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
    internal static string Text(string value, int maximum, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (!ContainsNoControlCharacters(value)) throw new ArgumentException("Control characters are not allowed.", parameter);
        var result = value.Trim();
        if (result.Length > maximum) throw new ArgumentException($"Maximum length is {maximum}.", parameter);
        return result;
    }
}

public sealed class WarehouseSite
{
    private WarehouseSite() { }
    public WarehouseSite(Guid id, string code, string name, WarehouseKind kind, string street, string city, string postalCode, string countryCode)
    {
        if (id == Guid.Empty) throw new ArgumentException("Warehouse ID is required.", nameof(id));
        if (!WarehouseRules.IsCode(code)) throw new ArgumentException("Invalid warehouse code.", nameof(code));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        Id = id;
        Code = code.Trim().ToUpperInvariant();
        Kind = kind;
        Update(name, street, city, postalCode, countryCode, true);
    }
    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public WarehouseKind Kind { get; private set; }
    public string Street { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string PostalCode { get; private set; } = string.Empty;
    public string CountryCode { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public void Update(string name, string street, string city, string postalCode, string countryCode, bool isActive)
    {
        var nextName = WarehouseRules.Text(name, 160, nameof(name));
        var nextStreet = WarehouseRules.Text(street, 200, nameof(street));
        var nextCity = WarehouseRules.Text(city, 100, nameof(city));
        var nextPostal = WarehouseRules.Text(postalCode, 20, nameof(postalCode));
        if (!WarehouseRules.IsCountryCode(countryCode)) throw new ArgumentException("Invalid country code.", nameof(countryCode));
        Name = nextName; Street = nextStreet; City = nextCity; PostalCode = nextPostal;
        CountryCode = countryCode.Trim().ToUpperInvariant(); IsActive = isActive;
    }
}

public sealed class ParcelInventory
{
    private ParcelInventory() { }
    public ParcelInventory(Guid shipmentId, string trackingNumber)
    {
        if (shipmentId == Guid.Empty) throw new ArgumentException("Shipment ID is required.", nameof(shipmentId));
        if (!WarehouseRules.IsTrackingNumber(trackingNumber)) throw new ArgumentException("Invalid tracking number.", nameof(trackingNumber));
        ShipmentId = shipmentId;
        TrackingNumber = WarehouseRules.NormalizeTrackingNumber(trackingNumber);
    }
    public Guid ShipmentId { get; private set; }
    public string TrackingNumber { get; private set; } = string.Empty;
    public Guid? CurrentWarehouseId { get; private set; }
    public bool HasBeenPickedUp { get; private set; }
    public DateTimeOffset? LastScanAtUtc { get; private set; }
    public long Revision { get; private set; }
    public bool ApplyScan(WarehouseSite warehouse, ScanDirection direction, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(warehouse);
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        var timestamp = WarehouseRules.NormalizeTimestamp(now);
        if (timestamp == default || LastScanAtUtc is { } previous && timestamp <= previous)
            throw new InvalidOperationException("Scan time must be later than the previous scan.");
        if (direction == ScanDirection.Entry)
        {
            if (!warehouse.IsActive) throw new InvalidOperationException("Warehouse is inactive.");
            if (CurrentWarehouseId is not null) throw new InvalidOperationException("Parcel is already in a warehouse.");
        }
        else if (CurrentWarehouseId != warehouse.Id) throw new InvalidOperationException("Parcel is not in this warehouse.");
        var firstPickup = direction == ScanDirection.Entry && !HasBeenPickedUp;
        CurrentWarehouseId = direction == ScanDirection.Entry ? warehouse.Id : null;
        if (firstPickup) HasBeenPickedUp = true;
        LastScanAtUtc = timestamp;
        Revision++;
        return firstPickup;
    }
}

public sealed class WarehouseScan
{
    private WarehouseScan() { }
    public WarehouseScan(Guid scanId, Guid shipmentId, Guid warehouseId, string trackingNumber, ScanDirection direction, DateTimeOffset occurredAtUtc, string operatorSubject)
    {
        if (scanId == Guid.Empty || shipmentId == Guid.Empty || warehouseId == Guid.Empty) throw new ArgumentException("Scan, shipment and warehouse IDs are required.");
        if (!WarehouseRules.IsTrackingNumber(trackingNumber)) throw new ArgumentException("Invalid tracking number.", nameof(trackingNumber));
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (occurredAtUtc == default) throw new ArgumentException("Scan time is required.", nameof(occurredAtUtc));
        ScanId = scanId; ShipmentId = shipmentId; WarehouseId = warehouseId;
        TrackingNumber = WarehouseRules.NormalizeTrackingNumber(trackingNumber);
        Direction = direction; OccurredAtUtc = WarehouseRules.NormalizeTimestamp(occurredAtUtc);
        OperatorSubject = WarehouseRules.Text(operatorSubject, 200, nameof(operatorSubject));
    }
    public Guid ScanId { get; private set; }
    public Guid ShipmentId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public string TrackingNumber { get; private set; } = string.Empty;
    public ScanDirection Direction { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string OperatorSubject { get; private set; } = string.Empty;
}
