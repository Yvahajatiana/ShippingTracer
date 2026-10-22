using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;

namespace ParcelFlow.Shipment.Infrastructure.Booking;

public sealed partial class ShipmentBookingStateMachine : MassTransitStateMachine<ShipmentBookingState>
{
    public State AwaitingPayment { get; private set; } = null!;
    public State AwaitingPickup { get; private set; } = null!;
    public State PaymentRejected { get; private set; } = null!;
    public State PaymentTimedOut { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public Event<ShipmentCreated> Created { get; private set; } = null!;
    public Event<PaymentCompleted> Paid { get; private set; } = null!;
    public Event<PaymentFailed> Rejected { get; private set; } = null!;
    public Event<ParcelPickedUp> PickedUp { get; private set; } = null!;
    public Schedule<ShipmentBookingState, ShipmentPaymentTimeoutExpired> PaymentTimeout { get; private set; } = null!;

    public ShipmentBookingStateMachine(IOptions<ShipmentBookingOptions> options, ILogger<ShipmentBookingStateMachine> logger)
    {
        var paymentTimeout = options.Value.PaymentTimeout;
        InstanceState(value => value.CurrentState);
        Event(() => Created, value => value.CorrelateById(context => context.Message.ShipmentId));
        Event(() => Paid, value => value.CorrelateById(context => context.Message.ShipmentId));
        Event(() => Rejected, value => value.CorrelateById(context => context.Message.ShipmentId));
        Event(() => PickedUp, value => value.CorrelateById(context => context.Message.ShipmentId));
        // The saga token makes a stale timeout (after payment or unscheduling) a no-op.
        Schedule(() => PaymentTimeout, value => value.PaymentTimeoutTokenId, schedule =>
        {
            schedule.Delay = paymentTimeout;
            schedule.Received = value => value.CorrelateById(context => context.Message.ShipmentId);
        });

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
            .Schedule(PaymentTimeout, context => new ShipmentPaymentTimeoutExpired(context.Message.ShipmentId,
                context.Message.CorrelationId))
            .TransitionTo(AwaitingPayment));

        During(AwaitingPayment,
            Ignore(Created), Ignore(PickedUp),
            When(Paid, context => context.Message.Amount == context.Saga.Amount &&
                    context.Message.Currency == context.Saga.Currency)
                .Unschedule(PaymentTimeout)
                .Activity(value => value.OfType<PaymentCompletedActivity>())
                .Publish(context => new ShipmentReadyForPickup(Guid.NewGuid(), context.Message.CorrelationId,
                    DateTimeOffset.UtcNow, context.Message.ShipmentId, context.Saga.TrackingNumber))
                .TransitionTo(AwaitingPickup),
            // A mismatched payment never authorizes pickup; the booking keeps waiting until the timeout.
            When(Paid, context => context.Message.Amount != context.Saga.Amount ||
                    context.Message.Currency != context.Saga.Currency)
                .Then(context => LogPaymentMismatch(logger, context.Saga.CorrelationId, context.Message.EventId,
                    context.Saga.Currency, context.Message.Currency, context.Message.Amount == context.Saga.Amount)),
            When(Rejected).Unschedule(PaymentTimeout)
                .Activity(value => value.OfType<PaymentFailedActivity>()).TransitionTo(PaymentRejected),
            When(PaymentTimeout.Received)
                .Then(context => LogPaymentTimeout(logger, context.Saga.CorrelationId, paymentTimeout))
                .Activity(value => value.OfType<PaymentTimeoutActivity>())
                .Publish(context => new ShipmentPaymentTimedOut(Guid.NewGuid(), context.Message.CorrelationId,
                    DateTimeOffset.UtcNow, context.Message.ShipmentId, context.Saga.TrackingNumber))
                .TransitionTo(PaymentTimedOut));
        During(AwaitingPickup,
            Ignore(Created), Ignore(Paid), Ignore(Rejected), Ignore(PaymentTimeout.Received),
            When(PickedUp).Activity(value => value.OfType<ParcelPickedUpActivity>()).TransitionTo(Completed));
        // Retain terminal rows: a repeated creation cannot initiate another payment.
        During(PaymentRejected, Ignore(Created), Ignore(Paid), Ignore(Rejected), Ignore(PickedUp), Ignore(PaymentTimeout.Received));
        During(PaymentTimedOut, Ignore(Created), Ignore(Rejected), Ignore(PickedUp), Ignore(PaymentTimeout.Received),
            // A capture arriving after the deadline needs a manual refund or review; it never reopens the booking.
            When(Paid).Then(context => LogLatePayment(logger, context.Saga.CorrelationId, context.Message.EventId)));
        During(Completed, Ignore(Created), Ignore(Paid), Ignore(Rejected), Ignore(PickedUp), Ignore(PaymentTimeout.Received));
    }

    [LoggerMessage(EventId = 3101, Level = LogLevel.Warning,
        Message = "Ignored PaymentCompleted {EventId} for shipment {ShipmentId}: expected currency {ExpectedCurrency}, received {ReceivedCurrency}, amount matches {AmountMatches}")]
    private static partial void LogPaymentMismatch(ILogger logger, Guid shipmentId, Guid eventId,
        string expectedCurrency, string receivedCurrency, bool amountMatches);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Warning,
        Message = "Payment for shipment {ShipmentId} was not confirmed within {PaymentTimeout}; booking closed as timed out")]
    private static partial void LogPaymentTimeout(ILogger logger, Guid shipmentId, TimeSpan paymentTimeout);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Warning,
        Message = "PaymentCompleted {EventId} arrived after the payment timeout of shipment {ShipmentId}; manual review is required")]
    private static partial void LogLatePayment(ILogger logger, Guid shipmentId, Guid eventId);
}
