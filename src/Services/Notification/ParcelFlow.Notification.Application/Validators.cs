using FluentValidation;

namespace ParcelFlow.Notification.Application;

public sealed class RecordNotificationValidator : AbstractValidator<RecordNotification>
{
    public RecordNotificationValidator()
    {
        RuleFor(x => x.EventId).NotEmpty(); RuleFor(x => x.ShipmentId).NotEmpty(); RuleFor(x => x.OccurredAtUtc).NotEqual(default(DateTimeOffset));
        RuleFor(x => x).Must(x => Domain.NotificationRules.IsPayload(x.Kind, x.CustomerSubject, x.TrackingNumber, x.Status,
            x.InvoiceId, x.CreditNoteId, x.Amount, x.Currency)).WithMessage("The event metadata must match its notification kind.");
        RuleFor(x => x).Must(x => !x.InvoiceId.HasValue || x.InvoiceId == x.ShipmentId).WithMessage("The invoice must identify the shipment.");
    }
}
public sealed class GetNotificationValidator : AbstractValidator<GetNotification>
{
    public GetNotificationValidator() => RuleFor(x => x.NotificationId).NotEmpty();
}
public sealed class ListNotificationsValidator : AbstractValidator<ListNotifications>
{
    public ListNotificationsValidator()
    {
        RuleFor(x => x.ShipmentId).Must(x => !x.HasValue || x != Guid.Empty);
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
