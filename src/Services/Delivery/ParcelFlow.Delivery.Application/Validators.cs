using FluentValidation;
using ParcelFlow.Delivery.Domain;

namespace ParcelFlow.Delivery.Application;

public sealed class CreateDriverValidator : AbstractValidator<CreateDriver>
{
    public CreateDriverValidator() { RuleFor(x => x.Subject).Must(x => DeliveryRules.IsText(x, 200)); RuleFor(x => x.DisplayName).Must(x => DeliveryRules.IsText(x, 120)); }
}
public sealed class UpdateDriverValidator : AbstractValidator<UpdateDriver>
{
    public UpdateDriverValidator() { RuleFor(x => x.Subject).Must(x => DeliveryRules.IsText(x, 200)); RuleFor(x => x.DisplayName).Must(x => DeliveryRules.IsText(x, 120)); }
}
public sealed class CreateRouteValidator : AbstractValidator<CreateRoute>
{
    public CreateRouteValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Name).Must(x => DeliveryRules.IsText(x, 120)); RuleFor(x => x.ServiceDate).NotEqual(default(DateOnly)); }
}
public sealed class AssignRouteDriverValidator : AbstractValidator<AssignRouteDriver>
{
    public AssignRouteDriverValidator() { RuleFor(x => x.RouteId).NotEmpty(); RuleFor(x => x.DriverSubject).Must(x => DeliveryRules.IsText(x, 200)); }
}
public sealed class AddRouteParcelValidator : AbstractValidator<AddRouteParcel>
{
    public AddRouteParcelValidator() { RuleFor(x => x.RouteId).NotEmpty(); RuleFor(x => x.ShipmentId).NotEmpty(); }
}
public sealed class RemoveRouteParcelValidator : AbstractValidator<RemoveRouteParcel>
{
    public RemoveRouteParcelValidator() { RuleFor(x => x.RouteId).NotEmpty(); RuleFor(x => x.ShipmentId).NotEmpty(); }
}
public sealed class StartRouteValidator : AbstractValidator<StartRoute>
{
    public StartRouteValidator() { RuleFor(x => x.RouteId).NotEmpty(); }
}
public sealed class RegisterReadyShipmentValidator : AbstractValidator<RegisterReadyShipment>
{
    public RegisterReadyShipmentValidator() { RuleFor(x => x.ShipmentId).NotEmpty(); RuleFor(x => x.TrackingNumber).NotEmpty().Must(x => x is not null && DeliveryRules.IsTrackingNumber(DeliveryRules.NormalizeTrackingNumber(x))); }
}
public sealed class RegisterParcelPickupValidator : AbstractValidator<RegisterParcelPickup>
{
    public RegisterParcelPickupValidator() { RuleFor(x => x.ShipmentId).NotEmpty(); }
}
public sealed class ConfirmDeliveryValidator : AbstractValidator<ConfirmDelivery>
{
    public ConfirmDeliveryValidator()
    {
        RuleFor(x => x.OperationId).NotEmpty(); RuleFor(x => x.RouteId).NotEmpty(); RuleFor(x => x.ShipmentId).NotEmpty();
        RuleFor(x => x.RecipientName).Must(x => DeliveryRules.IsText(x, 120));
        RuleFor(x => x.SignaturePng).Must(ProofImages.IsValidSignature).WithMessage("Signature must be a bounded PNG image.");
        RuleFor(x => x.Photo).Must(ProofImages.IsValidPhoto).WithMessage("Photo must be a bounded PNG or JPEG image.");
        RuleFor(x => x).Must(x => x.SignaturePng is not null || x.Photo is not null).WithMessage("At least one proof image is required.");
    }
}
public sealed class FailDeliveryValidator : AbstractValidator<FailDelivery>
{
    public FailDeliveryValidator() { RuleFor(x => x.OperationId).NotEmpty(); RuleFor(x => x.RouteId).NotEmpty(); RuleFor(x => x.ShipmentId).NotEmpty(); RuleFor(x => x.Reason).IsInEnum(); }
}
public sealed class GetRouteValidator : AbstractValidator<GetRoute> { public GetRouteValidator() { RuleFor(x => x.RouteId).NotEmpty(); } }
public sealed class GetDeliveryProofValidator : AbstractValidator<GetDeliveryProof> { public GetDeliveryProofValidator() { RuleFor(x => x.RouteId).NotEmpty(); RuleFor(x => x.ShipmentId).NotEmpty(); } }
public sealed class GetDeliveryProofImageValidator : AbstractValidator<GetDeliveryProofImage> { public GetDeliveryProofImageValidator() { RuleFor(x => x.RouteId).NotEmpty(); RuleFor(x => x.ShipmentId).NotEmpty(); RuleFor(x => x.Kind).IsInEnum(); } }
public sealed class ListRoutesValidator : AbstractValidator<ListRoutes> { public ListRoutesValidator() { RuleFor(x => x.Page).InclusiveBetween(1, 1000000); RuleFor(x => x.PageSize).InclusiveBetween(1, 100); } }
public sealed class ListDriversValidator : AbstractValidator<ListDrivers> { public ListDriversValidator() { RuleFor(x => x.Page).InclusiveBetween(1, 1000000); RuleFor(x => x.PageSize).InclusiveBetween(1, 100); } }
public sealed class ListAvailableParcelsValidator : AbstractValidator<ListAvailableParcels> { public ListAvailableParcelsValidator() { RuleFor(x => x.Page).InclusiveBetween(1, 1000000); RuleFor(x => x.PageSize).InclusiveBetween(1, 100); } }
