using FluentValidation;
using ParcelFlow.Shipment.Domain;

namespace ParcelFlow.Shipment.Application;

public sealed class AddressInputValidator : AbstractValidator<AddressInput>
{
    public AddressInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254)
            .Must(ShippingAddress.IsEmail)
            .WithMessage("Invalid email address.");
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Line2).MaximumLength(200).Must(x => x is null || !string.IsNullOrWhiteSpace(x));
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CountryCode).Must(ShippingAddress.IsCountryCode).WithMessage("Country must be an uppercase ISO 3166-1 alpha-2 code.");
        RuleFor(x => x).Must(x => new[] { x.Name, x.Email, x.Phone, x.Line1, x.Line2, x.PostalCode, x.City, x.CountryCode }
            .All(value => value is null || !value.Any(char.IsControl))).WithMessage("Address fields must not contain control characters.");
    }
}
public sealed class ParcelInputValidator : AbstractValidator<ParcelInput>
{
    public ParcelInputValidator()
    {
        RuleFor(x => x.WeightKg).GreaterThan(0).LessThanOrEqualTo(30).PrecisionScale(10, 3, true);
        RuleFor(x => x.LengthCm).GreaterThan(0).LessThanOrEqualTo(200).PrecisionScale(10, 2, true);
        RuleFor(x => x.WidthCm).GreaterThan(0).LessThanOrEqualTo(200).PrecisionScale(10, 2, true);
        RuleFor(x => x.HeightCm).GreaterThan(0).LessThanOrEqualTo(200).PrecisionScale(10, 2, true);
        RuleFor(x => x).Must(x => x.LengthCm is > 0 and <= 200 && x.WidthCm is > 0 and <= 200 &&
            x.HeightCm is > 0 and <= 200 && x.LengthCm * x.WidthCm * x.HeightCm / 5000m <= 30)
            .WithMessage("Volumetric weight must not exceed 30 kg.");
    }
}
public sealed class CreateShipmentValidator : AbstractValidator<CreateShipment>
{
    public CreateShipmentValidator()
    {
        RuleFor(x => x.Sender).NotNull().SetValidator(new AddressInputValidator());
        RuleFor(x => x.Recipient).NotNull().SetValidator(new AddressInputValidator());
        RuleFor(x => x.Parcel).NotNull().SetValidator(new ParcelInputValidator());
    }
}
public sealed class QuoteShipmentValidator : AbstractValidator<QuoteShipment>
{
    public QuoteShipmentValidator()
    {
        RuleFor(x => x.Sender).NotNull().SetValidator(new AddressInputValidator());
        RuleFor(x => x.Recipient).NotNull().SetValidator(new AddressInputValidator());
        RuleFor(x => x.Parcel).NotNull().SetValidator(new ParcelInputValidator());
    }
}
public sealed class GetShipmentValidator : AbstractValidator<GetShipment>
{
    public GetShipmentValidator() => RuleFor(x => x.ShipmentId).NotEmpty();
}
public sealed class GetLabelValidator : AbstractValidator<GetLabel>
{
    public GetLabelValidator() => RuleFor(x => x.ShipmentId).NotEmpty();
}
public sealed class ListShipmentsValidator : AbstractValidator<ListShipments>
{
    public ListShipmentsValidator()
    {
        RuleFor(x => x.Offset).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}
public sealed class UpdateTariffValidator : AbstractValidator<UpdateTariff>
{
    public UpdateTariffValidator()
    {
        RuleFor(x => x.Zone).IsInEnum();
        RuleFor(x => x.BasePrice).InclusiveBetween(0m, 10000m).PrecisionScale(7, 2, true);
        RuleFor(x => x.PricePerKg).GreaterThan(0).LessThanOrEqualTo(10000).PrecisionScale(7, 2, true);
    }
}
