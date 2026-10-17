using MassTransit;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;

namespace ParcelFlow.Shipment.Infrastructure.Booking;

public sealed class ShipmentBookingStateMachine : MassTransitStateMachine<ShipmentBookingState>
{
    public State AwaitingPayment { get; private set; } = null!;
    public State AwaitingPickup { get; private set; } = null!;
    public State PaymentRejected { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public Event<ShipmentCreated> Created { get; private set; } = null!;
    public Event<PaymentCompleted> Paid { get; private set; } = null!;
    public Event<PaymentFailed> Rejected { get; private set; } = null!;
    public Event<ParcelPickedUp> PickedUp { get; private set; } = null!;

    public ShipmentBookingStateMachine()
    {
        InstanceState(value => value.CurrentState);
        Event(() => Created, value => value.CorrelateById(context => context.Message.ShipmentId));
        Event(() => Paid, value => value.CorrelateById(context => context.Message.ShipmentId));
        Event(() => Rejected, value => value.CorrelateById(context => context.Message.ShipmentId));
        Event(() => PickedUp, value => value.CorrelateById(context => context.Message.ShipmentId));

        Initially(When(Created)
            .Then(context =>
            {
                context.Saga.TrackingNumber = context.Message.TrackingNumber;
                context.Saga.Amount = context.Message.Amount;
                context.Saga.Currency = context.Message.Currency;
            })
            .Send(new Uri("queue:billing-payments"), context => new PaymentRequested(Guid.NewGuid(), context.Message.CorrelationId,
                DateTimeOffset.UtcNow, context.Message.ShipmentId, context.Message.CustomerSubject,
                context.Message.Amount, context.Message.Currency))
            .TransitionTo(AwaitingPayment));

        During(AwaitingPayment,
            Ignore(Created), Ignore(PickedUp),
            When(Paid, context => context.Message.Amount == context.Saga.Amount &&
                    context.Message.Currency == context.Saga.Currency)
                .Activity(value => value.OfType<PaymentCompletedActivity>())
                .Publish(context => new ShipmentReadyForPickup(Guid.NewGuid(), context.Message.CorrelationId,
                    DateTimeOffset.UtcNow, context.Message.ShipmentId, context.Saga.TrackingNumber))
                .TransitionTo(AwaitingPickup),
            When(Paid, context => context.Message.Amount != context.Saga.Amount ||
                    context.Message.Currency != context.Saga.Currency).Then(_ => { }),
            When(Rejected).Activity(value => value.OfType<PaymentFailedActivity>()).TransitionTo(PaymentRejected));
        During(AwaitingPickup,
            Ignore(Created), Ignore(Paid), Ignore(Rejected),
            When(PickedUp).Activity(value => value.OfType<ParcelPickedUpActivity>()).TransitionTo(Completed));
        // Retain terminal rows: a repeated creation cannot initiate another payment.
        During(PaymentRejected, Ignore(Created), Ignore(Paid), Ignore(Rejected), Ignore(PickedUp));
        During(Completed, Ignore(Created), Ignore(Paid), Ignore(Rejected), Ignore(PickedUp));
    }
}
