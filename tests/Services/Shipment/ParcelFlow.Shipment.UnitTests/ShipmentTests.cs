using FluentAssertions;
using ParcelFlow.Shipment.Application;
using ParcelFlow.Shipment.Domain;
using Xunit;

namespace ParcelFlow.Shipment.UnitTests;

public sealed class ShipmentTests
{
    private static ShippingAddress Address(string country = "FR") => new("Alice Martin", "alice@example.test", "+33612345678", "8 rue de Paris", null, "75001", "Paris", country);
    private static ParcelShipment Shipment() => new("customer-demo", "PF0123456789ABCDEF01234567", Address(), Address(), new(1, 10, 10, 10), 7.50m, DeliveryZone.Domestic);

    [Theory]
    [InlineData("FR", "FR", DeliveryZone.Domestic)]
    [InlineData("FR", "DE", DeliveryZone.Europe)]
    [InlineData("FR", "GB", DeliveryZone.Europe)]
    [InlineData("FR", "MG", DeliveryZone.International)]
    [InlineData("US", "US", DeliveryZone.Domestic)]
    public void Zone_is_derived_from_countries(string origin, string destination, DeliveryZone expected) => ShippingZones.Resolve(origin, destination).Should().Be(expected);

    [Theory]
    [InlineData(0, 10, 10, 10)]
    [InlineData(31, 10, 10, 10)]
    [InlineData(1, 201, 10, 10)]
    [InlineData(1, 100, 100, 100)]
    [InlineData(1, 50, 50, 60.001)]
    public void Invalid_parcel_is_rejected(decimal weight, decimal length, decimal width, decimal height) =>
        FluentActions.Invoking(() => new ParcelSpecification(weight, length, width, height)).Should().Throw<ArgumentException>();

    [Fact]
    public void Tarif_uses_volumetric_weight_then_ceiling()
    {
        var parcel = new ParcelSpecification(1, 30, 20, 20);
        parcel.BillableWeightKg.Should().Be(2.4m);
        new Tariff(DeliveryZone.Domestic, 4m, 1.25m).CalculatePrice(parcel).Should().Be(7.75m);
    }

    [Fact]
    public void Tarif_preserves_cent_precision() =>
        new Tariff(DeliveryZone.Domestic, 1.01m, 1m).CalculatePrice(new(1, 1, 1, 1)).Should().Be(2.01m);

    [Theory]
    [InlineData(0.0001, 10, 10, 10)]
    [InlineData(1.0001, 10, 10, 10)]
    [InlineData(1, 10.001, 10, 10)]
    [InlineData(1, 10, 10.001, 10)]
    [InlineData(1, 10, 10, 10.001)]
    public void Parcel_excess_precision_is_rejected_by_domain_and_validator(decimal weight, decimal length, decimal width, decimal height)
    {
        FluentActions.Invoking(() => new ParcelSpecification(weight, length, width, height)).Should().Throw<ArgumentException>();
        new ParcelInputValidator().Validate(new ParcelInput(weight, length, width, height)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Parcel_accepts_storage_precision_and_trailing_zeroes()
    {
        new ParcelSpecification(1.0010m, 10.010m, 10.020m, 10.030m).WeightKg.Should().Be(1.001m);
        new ParcelInputValidator().Validate(new ParcelInput(1.0010m, 10.010m, 10.020m, 10.030m)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(1.001, 1)]
    [InlineData(1, 1.001)]
    public void Tariff_excess_precision_is_rejected_atomically(decimal basis, decimal perKg)
    {
        FluentActions.Invoking(() => new Tariff(DeliveryZone.Domestic, basis, perKg)).Should().Throw<ArgumentException>();
        var tariff = new Tariff(DeliveryZone.Domestic, 4m, 2m);
        FluentActions.Invoking(() => tariff.Update(basis, perKg)).Should().Throw<ArgumentException>();
        tariff.BasePrice.Should().Be(4m);
        tariff.PricePerKg.Should().Be(2m);
        new UpdateTariffValidator().Validate(new UpdateTariff(DeliveryZone.Domestic, basis, perKg)).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(1, -1)]
    public void Invalid_tariff_is_rejected(decimal basis, decimal perKg) => FluentActions.Invoking(() => new Tariff(DeliveryZone.Domestic, basis, perKg)).Should().Throw<ArgumentException>();

    [Fact]
    public void Failed_update_preserves_tariff()
    {
        var tariff = new Tariff(DeliveryZone.Domestic, 4m, 1m);
        FluentActions.Invoking(() => tariff.Update(8m, -1)).Should().Throw<ArgumentException>();
        tariff.BasePrice.Should().Be(4m);
    }

    [Fact]
    public void Paid_then_pickup_are_idempotent()
    {
        var shipment = Shipment();
        shipment.MarkPaid(); shipment.MarkPaid(); shipment.MarkPickedUp(); shipment.MarkPickedUp(); shipment.MarkPaid();
        shipment.Status.Should().Be(ShipmentStatus.PickedUp);
        FluentActions.Invoking(shipment.MarkPaymentFailed).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Unpaid_shipment_cannot_be_picked_up()
    {
        var shipment = Shipment();
        FluentActions.Invoking(shipment.MarkPickedUp).Should().Throw<InvalidOperationException>();
        shipment.MarkPaymentFailed(); shipment.MarkPaymentFailed();
        FluentActions.Invoking(shipment.MarkPaid).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(shipment.MarkPickedUp).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("pf0123456789ABCDEF01234567")]
    [InlineData("PF0123456789abcdef01234567")]
    [InlineData("PF0123456789ABCDEF0123456Z")]
    [InlineData("PF0123")]
    public void Invalid_tracking_number_is_rejected(string number) => FluentActions.Invoking(() =>
        new ParcelShipment("owner", number, Address(), Address(), new(1, 1, 1, 1), 7m, DeliveryZone.Domestic)).Should().Throw<ArgumentException>();

    [Fact]
    public void Address_rejects_unknown_country() => FluentActions.Invoking(() => Address("ZZ")).Should().Throw<ArgumentException>();

    [Theory]
    [InlineData("a@b..c")]
    [InlineData("a\"b@example.test")]
    [InlineData("a@<example.test>")]
    [InlineData("a b@example.test")]
    [InlineData(null)]
    public void Invalid_email_is_rejected_by_address_validation(string? email)
    {
        var input = new AddressInput("Alice Martin", email!, "+33612345678", "8 rue de Paris", null, "75001", "Paris", "FR");
        new AddressInputValidator().Validate(input).IsValid.Should().BeFalse();
        FluentActions.Invoking(() => input.ToDomain()).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Null_children_are_validation_errors()
    {
        new CreateShipmentValidator().Validate(new CreateShipment(null!, null!, null!)).Errors.Should().HaveCount(3);
        new QuoteShipmentValidator().Validate(new QuoteShipment(null!, null!, null!)).Errors.Should().HaveCount(3);
    }

    [Fact]
    public void Invalid_pagination_and_tariff_are_rejected()
    {
        new ListShipmentsValidator().Validate(new ListShipments(-1, 101)).IsValid.Should().BeFalse();
        new UpdateTariffValidator().Validate(new UpdateTariff((DeliveryZone)99, -1m, 1.001m)).Errors.Should().HaveCount(3);
    }
}
