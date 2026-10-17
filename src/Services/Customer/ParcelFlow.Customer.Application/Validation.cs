using FluentValidation;

namespace ParcelFlow.Customer.Application;

public sealed class CreateProfileValidator : AbstractValidator<CreateProfile>
{
    public CreateProfileValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254).EmailAddress().Must(value => System.Net.Mail.MailAddress.TryCreate(value, out var address) && address.Address == value);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30).When(x => x.Phone is not null);
    }
}

public sealed class UpdateProfileValidator : AbstractValidator<UpdateProfile>
{
    public UpdateProfileValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254).EmailAddress().Must(value => System.Net.Mail.MailAddress.TryCreate(value, out var address) && address.Address == value);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30).When(x => x.Phone is not null);
    }
}

public sealed class GetProfileValidator : AbstractValidator<GetProfile>
{
    public GetProfileValidator() => RuleFor(x => x.CustomerId).NotEmpty();
}

public sealed class ListProfilesValidator : AbstractValidator<ListProfiles>
{
    public ListProfilesValidator()
    {
        RuleFor(x => x.Offset).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}

public sealed class AddAddressValidator : AbstractValidator<AddAddress>
{
    public AddAddressValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Label).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Line2).NotEmpty().MaximumLength(200).When(x => x.Line2 is not null);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CountryCode).NotEmpty().Must(Domain.Address.IsCountryCode).WithMessage("Country must be an uppercase ISO 3166-1 alpha-2 code.");
    }
}

public sealed class UpdateAddressValidator : AbstractValidator<UpdateAddress>
{
    public UpdateAddressValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.AddressId).NotEmpty();
        RuleFor(x => x.Label).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Line2).NotEmpty().MaximumLength(200).When(x => x.Line2 is not null);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CountryCode).NotEmpty().Must(Domain.Address.IsCountryCode).WithMessage("Country must be an uppercase ISO 3166-1 alpha-2 code.");
    }
}

public sealed class DeleteAddressValidator : AbstractValidator<DeleteAddress>
{
    public DeleteAddressValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.AddressId).NotEmpty();
    }
}

public sealed class AssignCompanyValidator : AbstractValidator<AssignCompany>
{
    public AssignCompanyValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.CompanyId).NotEqual(Guid.Empty).When(x => x.CompanyId.HasValue);
    }
}

public sealed class CreateCompanyValidator : AbstractValidator<CreateCompany>
{
    public CreateCompanyValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RegistrationNumber).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254).EmailAddress().Must(value => System.Net.Mail.MailAddress.TryCreate(value, out var address) && address.Address == value);
    }
}

public sealed class UpdateCompanyValidator : AbstractValidator<UpdateCompany>
{
    public UpdateCompanyValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RegistrationNumber).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254).EmailAddress().Must(value => System.Net.Mail.MailAddress.TryCreate(value, out var address) && address.Address == value);
    }
}

public sealed class GetCompanyValidator : AbstractValidator<GetCompany>
{
    public GetCompanyValidator() => RuleFor(x => x.CompanyId).NotEmpty();
}
