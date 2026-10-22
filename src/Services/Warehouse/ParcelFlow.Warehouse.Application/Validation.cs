using FluentValidation;
using ParcelFlow.Warehouse.Domain;

namespace ParcelFlow.Warehouse.Application;

public sealed class CreateWarehouseValidator : AbstractValidator<CreateWarehouse>
{
    public CreateWarehouseValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Code).Must(WarehouseRules.IsCode).WithMessage("Invalid warehouse code.");
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160).Must(WarehouseRules.ContainsNoControlCharacters).WithMessage("Control characters are not allowed.");
        RuleFor(x => x.Street).NotEmpty().MaximumLength(200).Must(WarehouseRules.ContainsNoControlCharacters).WithMessage("Control characters are not allowed.");
        RuleFor(x => x.City).NotEmpty().MaximumLength(100).Must(WarehouseRules.ContainsNoControlCharacters).WithMessage("Control characters are not allowed.");
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20).Must(WarehouseRules.ContainsNoControlCharacters).WithMessage("Control characters are not allowed.");
        RuleFor(x => x.CountryCode).Must(WarehouseRules.IsCountryCode).WithMessage("Invalid country code.");
    }
}
public sealed class UpdateWarehouseValidator : AbstractValidator<UpdateWarehouse>
{
    public UpdateWarehouseValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160).Must(WarehouseRules.ContainsNoControlCharacters).WithMessage("Control characters are not allowed.");
        RuleFor(x => x.Street).NotEmpty().MaximumLength(200).Must(WarehouseRules.ContainsNoControlCharacters).WithMessage("Control characters are not allowed.");
        RuleFor(x => x.City).NotEmpty().MaximumLength(100).Must(WarehouseRules.ContainsNoControlCharacters).WithMessage("Control characters are not allowed.");
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20).Must(WarehouseRules.ContainsNoControlCharacters).WithMessage("Control characters are not allowed.");
        RuleFor(x => x.CountryCode).Must(WarehouseRules.IsCountryCode).WithMessage("Invalid country code.");
    }
}
public sealed class ScanParcelValidator : AbstractValidator<ScanParcel>
{
    public ScanParcelValidator()
    {
        RuleFor(x => x.ScanId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.TrackingNumber).Must(WarehouseRules.IsTrackingNumber).WithMessage("Invalid tracking number.");
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.OperatorSubject).NotEmpty().MaximumLength(200).Must(WarehouseRules.ContainsNoControlCharacters).WithMessage("Control characters are not allowed.");
    }
}
public sealed class RegisterReadyShipmentValidator : AbstractValidator<RegisterReadyShipment>
{
    public RegisterReadyShipmentValidator()
    {
        RuleFor(x => x.ShipmentId).NotEmpty();
        RuleFor(x => x.TrackingNumber).Must(WarehouseRules.IsTrackingNumber).WithMessage("Invalid tracking number.");
    }
}
public sealed class RecordDeliveryExitValidator : AbstractValidator<RecordDeliveryExit>
{
    public RecordDeliveryExitValidator()
    {
        RuleFor(x => x.SourceEventId).NotEmpty();
        RuleFor(x => x.ShipmentId).NotEmpty();
        RuleFor(x => x.TrackingNumber).Must(WarehouseRules.IsTrackingNumber).WithMessage("Invalid tracking number.");
        RuleFor(x => x.OccurredAtUtc).NotEqual(default(DateTimeOffset));
    }
}
public sealed class GetWarehouseValidator: AbstractValidator<GetWarehouse>
{
    public GetWarehouseValidator() => RuleFor(x => x.Id).NotEmpty();
}
public sealed class ListWarehousesValidator : AbstractValidator<ListWarehouses>
{
    public ListWarehousesValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
public sealed class GetWarehouseStockValidator : AbstractValidator<GetWarehouseStock>
{
    public GetWarehouseStockValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
