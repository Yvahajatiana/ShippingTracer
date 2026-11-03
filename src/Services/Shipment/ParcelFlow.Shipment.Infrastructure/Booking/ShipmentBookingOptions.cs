namespace ParcelFlow.Shipment.Infrastructure.Booking;

public sealed class ShipmentBookingOptions
{
    public const string SectionName = "ShipmentBooking";

    // The RabbitMQ delayed message exchange stores x-delay as an unsigned 32-bit millisecond value;
    // a longer delay is not honoured and the timeout would be routed immediately.
    public static readonly TimeSpan MaximumPaymentTimeout = TimeSpan.FromTicks(uint.MaxValue * TimeSpan.TicksPerMillisecond);

    // Maximum wait for a Billing result before the booking is closed as timed out.
    public TimeSpan PaymentTimeout { get; set; } = TimeSpan.FromMinutes(30);
}

// Internal message scheduled by the booking saga to itself; it is not a shared integration contract.
public sealed record ShipmentPaymentTimeoutExpired(Guid ShipmentId, Guid CorrelationId);
