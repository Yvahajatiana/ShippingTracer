using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Contracts.Customer.V1;
using ParcelFlow.Customer.Domain;

namespace ParcelFlow.Customer.Application;

internal sealed class CustomerAccess(ICustomerStore store, ICurrentUser user)
{
    public void RequireStaff()
    {
        if (!user.IsInRole("admin") && !user.IsInRole("operator")) throw new CustomerForbiddenException();
    }

    public void RequireAdmin()
    {
        if (!user.IsInRole("admin")) throw new CustomerForbiddenException();
    }

    public async Task<CustomerProfile> Profile(Guid id, bool allowOperator, CancellationToken ct)
    {
        var profile = await store.FindCustomerAsync(id, ct) ?? throw new CustomerNotFoundException("Customer profile not found.");
        if (profile.KeycloakSubject != user.Subject && !user.IsInRole("admin") && !(allowOperator && user.IsInRole("operator")))
            throw new CustomerForbiddenException();
        return profile;
    }

    public async Task<Company> Company(Guid id, CancellationToken ct) =>
        await store.FindCompanyAsync(id, ct) ?? throw new CustomerNotFoundException("Company not found.");
}

internal sealed class ProfileHandlers(ICustomerStore store, ICurrentUser user, IPublishEndpoint publisher, CustomerAccess access) :
    IRequestHandler<CreateProfile, ProfileDto>, IRequestHandler<UpdateProfile, ProfileDto>, IRequestHandler<GetProfile, ProfileDto>,
    IRequestHandler<GetMyProfile, ProfileDto>, IRequestHandler<ListProfiles, IReadOnlyList<ProfileDto>>, IRequestHandler<AssignCompany, ProfileDto>
{
    public async Task<ProfileDto> Handle(CreateProfile request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.Subject)) throw new CustomerForbiddenException();
        if (await store.FindBySubjectAsync(user.Subject, ct) is not null)
            throw new CustomerConflictException("A profile already exists for this identity.");
        var profile = new CustomerProfile(user.Subject, request.FirstName, request.LastName, request.Email, request.Phone);
        store.AddCustomer(profile);
        await publisher.Publish(new CustomerProfileCreated(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, profile.Id, profile.KeycloakSubject), ct);
        await store.SaveChangesAsync(ct);
        return profile.ToDto();
    }

    public async Task<ProfileDto> Handle(UpdateProfile request, CancellationToken ct)
    {
        var profile = await access.Profile(request.CustomerId, false, ct);
        profile.Update(request.FirstName, request.LastName, request.Email, request.Phone);
        await Changed(profile, ct);
        return profile.ToDto();
    }

    public async Task<ProfileDto> Handle(GetProfile request, CancellationToken ct) => (await access.Profile(request.CustomerId, true, ct)).ToDto();
    public async Task<ProfileDto> Handle(GetMyProfile request, CancellationToken ct) =>
        (await store.FindBySubjectAsync(user.Subject, ct) ?? throw new CustomerNotFoundException("Customer profile not found.")).ToDto();

    public async Task<IReadOnlyList<ProfileDto>> Handle(ListProfiles request, CancellationToken ct)
    {
        access.RequireStaff();
        return (await store.ListCustomersAsync(request.Offset, request.Limit, ct)).Select(p => p.ToDto()).ToArray();
    }

    public async Task<ProfileDto> Handle(AssignCompany request, CancellationToken ct)
    {
        access.RequireAdmin();
        var profile = await access.Profile(request.CustomerId, false, ct);
        if (request.CompanyId is { } id) await access.Company(id, ct);
        profile.AssignCompany(request.CompanyId);
        await Changed(profile, ct);
        return profile.ToDto();
    }

    private async Task Changed(CustomerProfile profile, CancellationToken ct)
    {
        await publisher.Publish(new CustomerProfileUpdated(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, profile.Id), ct);
        await store.SaveChangesAsync(ct);
    }
}

internal sealed class AddressHandlers(ICustomerStore store, IPublishEndpoint publisher, CustomerAccess access) :
    IRequestHandler<AddAddress, AddressDto>, IRequestHandler<UpdateAddress, AddressDto>, IRequestHandler<DeleteAddress>
{
    public async Task<AddressDto> Handle(AddAddress request, CancellationToken ct)
    {
        var profile = await access.Profile(request.CustomerId, true, ct);
        var address = profile.AddAddress(request.Label, request.Line1, request.Line2, request.PostalCode, request.City, request.CountryCode);
        await Changed(profile, ct);
        return address.ToDto();
    }

    public async Task<AddressDto> Handle(UpdateAddress request, CancellationToken ct)
    {
        var profile = await access.Profile(request.CustomerId, true, ct);
        var address = FindAddress(profile, request.AddressId);
        address.Update(request.Label, request.Line1, request.Line2, request.PostalCode, request.City, request.CountryCode);
        await Changed(profile, ct);
        return address.ToDto();
    }

    public async Task Handle(DeleteAddress request, CancellationToken ct)
    {
        var profile = await access.Profile(request.CustomerId, true, ct);
        var address = FindAddress(profile, request.AddressId);
        profile.RemoveAddress(address);
        store.RemoveAddress(address);
        await Changed(profile, ct);
    }

    private static Address FindAddress(CustomerProfile profile, Guid id) =>
        profile.Addresses.SingleOrDefault(a => a.Id == id) ?? throw new CustomerNotFoundException("Address not found.");

    private async Task Changed(CustomerProfile profile, CancellationToken ct)
    {
        await publisher.Publish(new CustomerProfileUpdated(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, profile.Id), ct);
        await store.SaveChangesAsync(ct);
    }
}

internal sealed class CompanyHandlers(ICustomerStore store, IPublishEndpoint publisher, CustomerAccess access) :
    IRequestHandler<CreateCompany, CompanyDto>, IRequestHandler<UpdateCompany, CompanyDto>, IRequestHandler<GetCompany, CompanyDto>,
    IRequestHandler<ListCompanies, IReadOnlyList<CompanyDto>>
{
    public async Task<CompanyDto> Handle(CreateCompany request, CancellationToken ct)
    {
        access.RequireAdmin();
        var company = new Company(request.Name, request.RegistrationNumber, request.Email);
        store.AddCompany(company);
        await publisher.Publish(new CompanyCreated(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, company.Id), ct);
        await store.SaveChangesAsync(ct);
        return company.ToDto();
    }

    public async Task<CompanyDto> Handle(UpdateCompany request, CancellationToken ct)
    {
        access.RequireAdmin();
        var company = await access.Company(request.CompanyId, ct);
        company.Update(request.Name, request.RegistrationNumber, request.Email);
        await publisher.Publish(new CompanyUpdated(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, company.Id), ct);
        await store.SaveChangesAsync(ct);
        return company.ToDto();
    }

    public async Task<CompanyDto> Handle(GetCompany request, CancellationToken ct)
    {
        access.RequireStaff();
        return (await access.Company(request.CompanyId, ct)).ToDto();
    }

    public async Task<IReadOnlyList<CompanyDto>> Handle(ListCompanies request, CancellationToken ct)
    {
        access.RequireStaff();
        return (await store.ListCompaniesAsync(ct)).Select(c => c.ToDto()).ToArray();
    }
}
