using System.Net.Mail;

namespace ParcelFlow.Shipment.Domain;

public enum DeliveryZone { Domestic, Europe, International }
public enum ShipmentStatus { AwaitingPayment, ReadyForPickup, PaymentFailed, PickedUp }

internal static class Guard
{
    internal static string Text(string value, int maximum, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum || value.Any(char.IsControl))
            throw new ArgumentException($"{name} must contain between 1 and {maximum} characters.", name);
        return value.Trim();
    }
}

public sealed class ShippingAddress
{
    private static readonly HashSet<string> CountryCodes = new(
        ("AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ " +
         "CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR " +
         "GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM JO JP " +
         "KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR MS MT MU MV MW MX MY MZ " +
         "NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO RS RU RW " +
         "SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT TV TW TZ " +
         "UA UG UM US UY UZ VA VC VE VG VI VN VU WF WS YE YT ZA ZM ZW").Split(' '), StringComparer.Ordinal);

    private ShippingAddress() { }
    public ShippingAddress(string name, string email, string phone, string line1, string? line2, string postalCode, string city, string countryCode)
    {
        Name = Guard.Text(name, 100, nameof(name));
        Email = Guard.Text(email, 254, nameof(email));
        if (!IsEmail(Email))
            throw new ArgumentException("Invalid email address.", nameof(email));
        Phone = Guard.Text(phone, 30, nameof(phone));
        Line1 = Guard.Text(line1, 200, nameof(line1));
        Line2 = line2 is null ? null : Guard.Text(line2, 200, nameof(line2));
        PostalCode = Guard.Text(postalCode, 20, nameof(postalCode));
        City = Guard.Text(city, 100, nameof(city));
        CountryCode = Guard.Text(countryCode, 2, nameof(countryCode));
        if (!IsCountryCode(CountryCode)) throw new ArgumentException("Country must be an uppercase ISO 3166-1 alpha-2 code.", nameof(countryCode));
    }
    public static bool IsCountryCode(string? value) => value is not null && CountryCodes.Contains(value);
    public static bool IsEmail(string? value)
    {
        if (!MailAddress.TryCreate(value, out var address) || address.Address != value) return false;
        var host = address.Host;
        return host.StartsWith('[') && host.EndsWith(']') ||
            !host.StartsWith('.') && !host.EndsWith('.') && !host.Contains("..", StringComparison.Ordinal);
    }
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string Line1 { get; private set; } = string.Empty;
    public string? Line2 { get; private set; }
    public string PostalCode { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string CountryCode { get; private set; } = string.Empty;
}

public sealed class ParcelSpecification
{
    private ParcelSpecification() { }
    public ParcelSpecification(decimal weightKg, decimal lengthCm, decimal widthCm, decimal heightCm)
    {
        if (weightKg is <= 0 or > 30) throw new ArgumentOutOfRangeException(nameof(weightKg), "Weight must be greater than zero and at most 30 kg.");
        if (lengthCm is <= 0 or > 200) throw new ArgumentOutOfRangeException(nameof(lengthCm));
        if (widthCm is <= 0 or > 200) throw new ArgumentOutOfRangeException(nameof(widthCm));
        if (heightCm is <= 0 or > 200) throw new ArgumentOutOfRangeException(nameof(heightCm));
        if (weightKg != decimal.Round(weightKg, 3)) throw new ArgumentException("Weight accepts at most three decimal places.", nameof(weightKg));
        if (lengthCm != decimal.Round(lengthCm, 2)) throw new ArgumentException("Length accepts at most two decimal places.", nameof(lengthCm));
        if (widthCm != decimal.Round(widthCm, 2)) throw new ArgumentException("Width accepts at most two decimal places.", nameof(widthCm));
        if (heightCm != decimal.Round(heightCm, 2)) throw new ArgumentException("Height accepts at most two decimal places.", nameof(heightCm));
        (WeightKg, LengthCm, WidthCm, HeightCm) = (weightKg, lengthCm, widthCm, heightCm);
        if (BillableWeightKg > 30) throw new ArgumentException("Volumetric weight must not exceed 30 kg.");
    }
    public decimal WeightKg { get; private set; }
    public decimal LengthCm { get; private set; }
    public decimal WidthCm { get; private set; }
    public decimal HeightCm { get; private set; }
    public decimal BillableWeightKg => Math.Max(WeightKg, LengthCm * WidthCm * HeightCm / 5000m);
}

public sealed class Tariff
{
    private Tariff() { }
    public Tariff(DeliveryZone zone, decimal basePrice, decimal pricePerKg)
    {
        if (!Enum.IsDefined(zone)) throw new ArgumentOutOfRangeException(nameof(zone));
        Zone = zone;
        Update(basePrice, pricePerKg);
    }
    public DeliveryZone Zone { get; private set; }
    public decimal BasePrice { get; private set; }
    public decimal PricePerKg { get; private set; }
    public string Currency => "EUR";
    public void Update(decimal basePrice, decimal pricePerKg)
    {
        if (basePrice is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(basePrice));
        if (pricePerKg is <= 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(pricePerKg));
        if (basePrice != decimal.Round(basePrice, 2)) throw new ArgumentException("Base price accepts at most two decimal places.", nameof(basePrice));
        if (pricePerKg != decimal.Round(pricePerKg, 2)) throw new ArgumentException("Price per kilogram accepts at most two decimal places.", nameof(pricePerKg));
        (BasePrice, PricePerKg) = (basePrice, pricePerKg);
    }
    public decimal CalculatePrice(ParcelSpecification parcel) => decimal.Round(BasePrice + decimal.Ceiling(parcel.BillableWeightKg) * PricePerKg, 2, MidpointRounding.AwayFromZero);
}

public static class ShippingZones
{
    // Geographical Europe, including transcontinental countries and European territories.
    private static readonly HashSet<string> Europe = new(
        "AD AL AM AT AX AZ BA BE BG BY CH CY CZ DE DK EE ES FI FO FR GB GE GG GI GR HR HU IE IM IS IT JE LI LT LU LV MC MD ME MK MT NL NO PL PT RO RS RU SE SI SJ SK SM TR UA VA".Split(' '), StringComparer.Ordinal);
    public static DeliveryZone Resolve(string origin, string destination)
    {
        if (!ShippingAddress.IsCountryCode(origin) || !ShippingAddress.IsCountryCode(destination)) throw new ArgumentException("Invalid country code.");
        return origin == destination ? DeliveryZone.Domestic : Europe.Contains(origin) && Europe.Contains(destination) ? DeliveryZone.Europe : DeliveryZone.International;
    }
}

public sealed class ParcelShipment
{
    private ParcelShipment() { }
    public ParcelShipment(string ownerSubject, string trackingNumber, ShippingAddress sender, ShippingAddress recipient, ParcelSpecification parcel, decimal price, DeliveryZone zone)
    {
        OwnerSubject = Guard.Text(ownerSubject, 200, nameof(ownerSubject));
        if (trackingNumber is null || trackingNumber.Length != 26 || !trackingNumber.StartsWith("PF", StringComparison.Ordinal) || trackingNumber[2..].Any(c => !"0123456789ABCDEF".Contains(c)))
            throw new ArgumentException("Tracking number must be PF followed by 24 uppercase hexadecimal characters.", nameof(trackingNumber));
        if (price <= 0 || price != decimal.Round(price, 2)) throw new ArgumentOutOfRangeException(nameof(price));
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(parcel);
        if (!Enum.IsDefined(zone) || zone != ShippingZones.Resolve(sender.CountryCode, recipient.CountryCode)) throw new ArgumentException("Shipment zone does not match its addresses.", nameof(zone));
        (Id, TrackingNumber, Sender, Recipient, Parcel, Price, Zone, CreatedAtUtc) = (Guid.NewGuid(), trackingNumber, sender, recipient, parcel, price, zone, DateTimeOffset.UtcNow);
    }
    public Guid Id { get; private set; }
    public string OwnerSubject { get; private set; } = string.Empty;
    public string TrackingNumber { get; private set; } = string.Empty;
    public ShippingAddress Sender { get; private set; } = null!;
    public ShippingAddress Recipient { get; private set; } = null!;
    public ParcelSpecification Parcel { get; private set; } = null!;
    public decimal Price { get; private set; }
    public DeliveryZone Zone { get; private set; }
    public string Currency => "EUR";
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public ShipmentStatus Status { get; private set; } = ShipmentStatus.AwaitingPayment;
    public void MarkPaid()
    {
        if (Status is ShipmentStatus.ReadyForPickup or ShipmentStatus.PickedUp) return;
        if (Status != ShipmentStatus.AwaitingPayment) throw new InvalidOperationException("Payment cannot succeed in the current state.");
        Status = ShipmentStatus.ReadyForPickup;
    }
    public void MarkPaymentFailed()
    {
        if (Status == ShipmentStatus.PaymentFailed) return;
        if (Status != ShipmentStatus.AwaitingPayment) throw new InvalidOperationException("Payment cannot fail in the current state.");
        Status = ShipmentStatus.PaymentFailed;
    }
    public void MarkPickedUp()
    {
        if (Status == ShipmentStatus.PickedUp) return;
        if (Status != ShipmentStatus.ReadyForPickup) throw new InvalidOperationException("Only paid shipments can be picked up.");
        Status = ShipmentStatus.PickedUp;
    }
}
