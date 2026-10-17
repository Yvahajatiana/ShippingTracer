using System.Net.Mail;

namespace ParcelFlow.Customer.Domain;

internal static class Guard
{
    internal static string Text(string value, int maximum, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum)
            throw new ArgumentException($"{name} must contain between 1 and {maximum} characters.", name);
        return value.Trim();
    }

    internal static string? Optional(string? value, int maximum, string name) =>
        value is null ? null : Text(value, maximum, name);

    internal static string Email(string value)
    {
        value = Text(value, 254, nameof(value));
        if (!MailAddress.TryCreate(value, out var address) || address.Address != value)
            throw new ArgumentException("Invalid email address.", nameof(value));
        return value;
    }

    internal static Guid Id(Guid value, string name) => value == Guid.Empty
        ? throw new ArgumentException("An identifier cannot be empty.", name) : value;
}

public sealed class CustomerProfile
{
    private readonly List<Address> _addresses = [];
    private CustomerProfile() { }

    public CustomerProfile(string keycloakSubject, string firstName, string lastName, string email, string? phone)
    {
        Id = Guid.NewGuid();
        KeycloakSubject = Guard.Text(keycloakSubject, 200, nameof(keycloakSubject));
        Update(firstName, lastName, email, phone);
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string KeycloakSubject { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public Guid? CompanyId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public IReadOnlyCollection<Address> Addresses => _addresses.AsReadOnly();

    public void Update(string firstName, string lastName, string email, string? phone)
    {
        var validFirst = Guard.Text(firstName, 100, nameof(firstName));
        var validLast = Guard.Text(lastName, 100, nameof(lastName));
        var validEmail = Guard.Email(email);
        var validPhone = Guard.Optional(phone, 30, nameof(phone));
        (FirstName, LastName, Email, Phone) = (validFirst, validLast, validEmail, validPhone);
    }

    public void AssignCompany(Guid? companyId) => CompanyId = companyId is { } id ? Guard.Id(id, nameof(companyId)) : null;

    public Address AddAddress(string label, string line1, string? line2, string postalCode, string city, string countryCode)
    {
        var address = new Address(Id, label, line1, line2, postalCode, city, countryCode);
        _addresses.Add(address);
        return address;
    }

    public void RemoveAddress(Address address)
    {
        if (address.CustomerId != Id || !_addresses.Remove(address))
            throw new ArgumentException("The address does not belong to this customer.", nameof(address));
    }
}

public sealed class Address
{
    private static readonly HashSet<string> CountryCodes = new(
        ("AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ " +
         "CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR " +
         "GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM JO JP " +
         "KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR MS MT MU MV MW MX MY MZ " +
         "NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO RS RU RW " +
         "SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT TV TW TZ " +
         "UA UG UM US UY UZ VA VC VE VG VI VN VU WF WS YE YT ZA ZM ZW").Split(' '), StringComparer.Ordinal);

    public static bool IsCountryCode(string? value) => value is not null && CountryCodes.Contains(value);
    private Address() { }

    public Address(Guid customerId, string label, string line1, string? line2, string postalCode, string city, string countryCode)
    {
        Id = Guid.NewGuid();
        CustomerId = Guard.Id(customerId, nameof(customerId));
        Update(label, line1, line2, postalCode, city, countryCode);
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public string Line1 { get; private set; } = string.Empty;
    public string? Line2 { get; private set; }
    public string PostalCode { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string CountryCode { get; private set; } = string.Empty;

    public void Update(string label, string line1, string? line2, string postalCode, string city, string countryCode)
    {
        var validLabel = Guard.Text(label, 80, nameof(label));
        var validLine1 = Guard.Text(line1, 200, nameof(line1));
        var validLine2 = Guard.Optional(line2, 200, nameof(line2));
        var validPostal = Guard.Text(postalCode, 20, nameof(postalCode));
        var validCity = Guard.Text(city, 100, nameof(city));
        var validCountry = Guard.Text(countryCode, 2, nameof(countryCode));
        if (!IsCountryCode(validCountry))
            throw new ArgumentException("Country must be an uppercase ISO 3166-1 alpha-2 code.", nameof(countryCode));
        (Label, Line1, Line2, PostalCode, City, CountryCode) =
            (validLabel, validLine1, validLine2, validPostal, validCity, validCountry);
    }
}

public sealed class Company
{
    private Company() { }
    public Company(string name, string registrationNumber, string email)
    {
        Id = Guid.NewGuid();
        Update(name, registrationNumber, email);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string RegistrationNumber { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;

    public void Update(string name, string registrationNumber, string email)
    {
        var validName = Guard.Text(name, 200, nameof(name));
        var validRegistration = Guard.Text(registrationNumber, 80, nameof(registrationNumber)).ToUpperInvariant();
        var validEmail = Guard.Email(email);
        (Name, RegistrationNumber, Email) = (validName, validRegistration, validEmail);
    }
}
