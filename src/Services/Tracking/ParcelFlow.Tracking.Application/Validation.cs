using FluentValidation;
using ParcelFlow.Tracking.Domain;

namespace ParcelFlow.Tracking.Application;

public sealed class RecordTrackingEventValidator : AbstractValidator<RecordTrackingEvent>
{
    public RecordTrackingEventValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.ShipmentId).NotEmpty();
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.OccurredAtUtc).NotEqual(default(DateTimeOffset));
        RuleFor(x => x.TrackingNumber).Must(x => x is null || TrackingRules.IsTrackingNumber(x)).WithMessage("Invalid tracking number.");
        RuleFor(x => x.FacilityCode).Must(TrackingRules.IsFacilityCode).WithMessage("Invalid facility code.");
    }
}
public sealed class GetPublicTrackingValidator : AbstractValidator<GetPublicTracking>
{
    public GetPublicTrackingValidator() => RuleFor(x => x.TrackingNumber).Must(TrackingRules.IsTrackingNumber).WithMessage("Invalid tracking number.");
}
public sealed class GetShipmentTrackingValidator : AbstractValidator<GetShipmentTracking>
{
    public GetShipmentTrackingValidator() => RuleFor(x => x.ShipmentId).NotEmpty();
}
