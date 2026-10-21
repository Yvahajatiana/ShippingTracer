using FluentValidation;
using ParcelFlow.Reporting.Domain;

namespace ParcelFlow.Reporting.Application;

public sealed class RecordStatusFactValidator : AbstractValidator<RecordStatusFact>
{
    public RecordStatusFactValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.SourceEventId).NotEmpty();
        RuleFor(x => x.ShipmentId).NotEmpty();
        RuleFor(x => x.TrackingNumber).Must(ReportingRules.IsTrackingNumber);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.OccurredAtUtc).NotEmpty();
        RuleFor(x => x.FacilityCode).Must(ReportingRules.IsFacilityCode);
    }
}
public sealed class GetKpisValidator : AbstractValidator<GetKpis>
{
    public GetKpisValidator()
    {
        RuleFor(x => x).Must(x => x.To.DayNumber - x.From.DayNumber is >= 1 and <= 366)
            .WithMessage("The UTC cohort interval must contain between 1 and 366 days; To is exclusive.");
    }
}
