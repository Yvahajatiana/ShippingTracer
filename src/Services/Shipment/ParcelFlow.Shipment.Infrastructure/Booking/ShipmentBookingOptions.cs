namespace ParcelFlow.Shipment.Infrastructure.Booking;

public sealed class ShipmentBookingOptions
{
    public const string SectionName = "ShipmentBooking";

    // Maximum wait for a Billing result before the booking is closed as timed out.
    public TimeSpan PaymentTimeout { get; set; } = TimeSpan.FromMinutes(30);
}

// Internal message scheduled by the booking saga to itself; it is not a shared integration contract.
public sealed record ShipmentPaymentTimeoutExpired(Guid ShipmentId, Guid CorrelationId);
