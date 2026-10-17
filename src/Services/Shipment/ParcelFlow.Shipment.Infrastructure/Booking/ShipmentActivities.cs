using MassTransit;
using Microsoft.EntityFrameworkCore;
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.Shipment.Domain;
using ParcelFlow.Shipment.Infrastructure.Persistence;

namespace ParcelFlow.Shipment.Infrastructure.Booking;

public abstract class ShipmentActivity<TMessage>(ShipmentDbContext database)
    : IStateMachineActivity<ShipmentBookingState, TMessage> where TMessage : class
{
    protected abstract void Apply(ParcelShipment shipment);

    public async Task Execute(BehaviorContext<ShipmentBookingState, TMessage> context,
        IBehavior<ShipmentBookingState, TMessage> next)
    {
        var shipment = await database.Shipments.SingleAsync(value => value.Id == context.Saga.CorrelationId,
            context.CancellationToken);
        Apply(shipment);
        // Saga, shipment changes and outgoing events are saved by the same EF transaction.
        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<ShipmentBookingState, TMessage, TException> context,
        IBehavior<ShipmentBookingState, TMessage> next) where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope(GetType().Name);
    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

public sealed class PaymentCompletedActivity(ShipmentDbContext database) : ShipmentActivity<PaymentCompleted>(database)
{
    protected override void Apply(ParcelShipment shipment) => shipment.MarkPaid();
}

public sealed class PaymentFailedActivity(ShipmentDbContext database) : ShipmentActivity<PaymentFailed>(database)
{
    protected override void Apply(ParcelShipment shipment) => shipment.MarkPaymentFailed();
}

public sealed class ParcelPickedUpActivity(ShipmentDbContext database) : ShipmentActivity<ParcelPickedUp>(database)
{
    protected override void Apply(ParcelShipment shipment) => shipment.MarkPickedUp();
}
