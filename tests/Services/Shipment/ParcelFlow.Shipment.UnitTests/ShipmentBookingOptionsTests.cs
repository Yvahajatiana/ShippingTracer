using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ParcelFlow.Shipment.Infrastructure;
using ParcelFlow.Shipment.Infrastructure.Booking;
using Xunit;

namespace ParcelFlow.Shipment.UnitTests;

public sealed class ShipmentBookingOptionsTests
{
    private static ShipmentBookingOptions Resolve(string? paymentTimeout)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ShipmentBooking:PaymentTimeout"] = paymentTimeout
        }).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);
        services.AddShipmentInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ShipmentBookingOptions>>().Value;
    }

    private static string Format(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);

    [Fact]
    public void Payment_timeout_defaults_to_thirty_minutes() =>
        Resolve(null).PaymentTimeout.Should().Be(TimeSpan.FromMinutes(30));

    [Fact]
    public void Maximum_payment_timeout_matches_the_delayed_exchange_limit_and_is_accepted()
    {
        ShipmentBookingOptions.MaximumPaymentTimeout.TotalMilliseconds.Should().Be(uint.MaxValue);
        Resolve(Format(ShipmentBookingOptions.MaximumPaymentTimeout)).PaymentTimeout
            .Should().Be(ShipmentBookingOptions.MaximumPaymentTimeout);
    }

    [Fact]
    public void Payment_timeout_above_the_delayed_exchange_limit_is_rejected()
    {
        var action = () => Resolve(Format(ShipmentBookingOptions.MaximumPaymentTimeout + TimeSpan.FromMilliseconds(1)));
        action.Should().Throw<OptionsValidationException>().WithMessage("*must not exceed*");
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:01:00")]
    public void Non_positive_payment_timeout_is_rejected(string value)
    {
        var action = () => Resolve(value);
        action.Should().Throw<OptionsValidationException>().WithMessage("*must be positive*");
    }
}
