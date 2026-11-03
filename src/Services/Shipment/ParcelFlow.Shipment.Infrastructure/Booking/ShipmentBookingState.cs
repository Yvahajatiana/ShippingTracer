using MassTransit;

namespace ParcelFlow.Shipment.Infrastructure.Booking;

public sealed class ShipmentBookingState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public Guid? PaymentTimeoutTokenId { get; set; }
    public uint RowVersion { get; set; }
}
