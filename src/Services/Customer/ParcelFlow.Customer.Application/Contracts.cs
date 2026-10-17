using MediatR;
using ParcelFlow.Customer.Domain;

namespace ParcelFlow.Customer.Application;

public interface ICustomerStore
{
    Task<CustomerProfile?> FindCustomerAsync(Guid id, CancellationToken cancellationToken);
    Task<CustomerProfile?> FindBySubjectAsync(string subject, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerProfile>> ListCustomersAsync(int offset, int limit, CancellationToken cancellationToken);
    Task<Company?> FindCompanyAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Company>> ListCompaniesAsync(CancellationToken cancellationToken);
    void AddCustomer(CustomerProfile profile);
    void AddCompany(Company company);
    void RemoveAddress(Address address);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ICurrentUser
{
    string Subject { get; }
    bool IsInRole(string role);
}

public sealed class CustomerNotFoundException(string message) : Exception(message);
public sealed class CustomerConflictException(string message) : Exception(message);
public sealed class CustomerForbiddenException() : Exception("You do not have access to this resource.");

public sealed record AddressDto(Guid Id, string Label, string Line1, string? Line2, string PostalCode, string City, string CountryCode);
public sealed record ProfileDto(Guid Id, string KeycloakSubject, string FirstName, string LastName, string Email, string? Phone,
    Guid? CompanyId, DateTimeOffset CreatedAtUtc, IReadOnlyList<AddressDto> Addresses);
public sealed record CompanyDto(Guid Id, string Name, string RegistrationNumber, string Email);

public sealed record CreateProfile(string FirstName, string LastName, string Email, string? Phone) : IRequest<ProfileDto>;
public sealed record UpdateProfile(Guid CustomerId, string FirstName, string LastName, string Email, string? Phone) : IRequest<ProfileDto>;
public sealed record GetProfile(Guid CustomerId) : IRequest<ProfileDto>;
public sealed record GetMyProfile : IRequest<ProfileDto>;
public sealed record ListProfiles(int Offset = 0, int Limit = 20) : IRequest<IReadOnlyList<ProfileDto>>;
public sealed record AddAddress(Guid CustomerId, string Label, string Line1, string? Line2, string PostalCode, string City, string CountryCode) : IRequest<AddressDto>;
public sealed record UpdateAddress(Guid CustomerId, Guid AddressId, string Label, string Line1, string? Line2, string PostalCode, string City, string CountryCode) : IRequest<AddressDto>;
public sealed record DeleteAddress(Guid CustomerId, Guid AddressId) : IRequest;
public sealed record AssignCompany(Guid CustomerId, Guid? CompanyId) : IRequest<ProfileDto>;
public sealed record CreateCompany(string Name, string RegistrationNumber, string Email) : IRequest<CompanyDto>;
public sealed record UpdateCompany(Guid CompanyId, string Name, string RegistrationNumber, string Email) : IRequest<CompanyDto>;
public sealed record GetCompany(Guid CompanyId) : IRequest<CompanyDto>;
public sealed record ListCompanies : IRequest<IReadOnlyList<CompanyDto>>;

public static class CustomerMapping
{
    public static ProfileDto ToDto(this CustomerProfile value) => new(value.Id, value.KeycloakSubject, value.FirstName, value.LastName,
        value.Email, value.Phone, value.CompanyId, value.CreatedAtUtc, value.Addresses.OrderBy(a => a.Label).Select(a => a.ToDto()).ToArray());
    public static AddressDto ToDto(this Address value) => new(value.Id, value.Label, value.Line1, value.Line2, value.PostalCode, value.City, value.CountryCode);
    public static CompanyDto ToDto(this Company value) => new(value.Id, value.Name, value.RegistrationNumber, value.Email);
}
