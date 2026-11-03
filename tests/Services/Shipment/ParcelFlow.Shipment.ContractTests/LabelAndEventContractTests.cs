using System.Text;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.Shipment.Domain;
using ParcelFlow.Shipment.Infrastructure.Labels;
using Xunit;

namespace ParcelFlow.Shipment.ContractTests;

public sealed class LabelAndEventContractTests
{
    [Fact]
    public void Shipment_creation_wire_contract_keeps_its_financial_and_correlation_fields()
    {
        const string json = """
        {"EventId":"11111111-1111-1111-1111-111111111111","CorrelationId":"22222222-2222-2222-2222-222222222222","OccurredAtUtc":"2026-10-17T12:00:00+00:00","ShipmentId":"22222222-2222-2222-2222-222222222222","TrackingNumber":"PF0123456789ABCDEF01234567","CustomerSubject":"customer-one","Amount":16.20,"Currency":"EUR"}
        """;
        var message = JsonSerializer.Deserialize<ShipmentCreated>(json)!;
        message.ShipmentId.Should().Be(message.CorrelationId);
        message.Amount.Should().Be(16.20m);
        message.CustomerSubject.Should().Be("customer-one");
        var roundtrip = JsonSerializer.Deserialize<ShipmentCreated>(JsonSerializer.Serialize(message));
        roundtrip.Should().Be(message);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(message));
        document.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "EventId", "CorrelationId", "OccurredAtUtc", "ShipmentId", "TrackingNumber", "CustomerSubject", "Amount", "Currency");
    }

    [Fact]
    public void Payment_completion_wire_contract_roundtrips_without_losing_decimal_precision()
    {
        var id = Guid.NewGuid();
        var message = new PaymentCompleted(Guid.NewGuid(), id, DateTimeOffset.UtcNow, id, 1234.56m, "EUR");
        JsonSerializer.Deserialize<PaymentCompleted>(JsonSerializer.Serialize(message)).Should().Be(message);
    }

    [Fact]
    public void Payment_timeout_wire_contract_exposes_no_customer_or_financial_data()
    {
        var id = Guid.NewGuid();
        var message = new ShipmentPaymentTimedOut(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, id,
            "PF0123456789ABCDEF01234567");
        JsonSerializer.Deserialize<ShipmentPaymentTimedOut>(JsonSerializer.Serialize(message)).Should().Be(message);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(message));
        document.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "EventId", "CorrelationId", "OccurredAtUtc", "ShipmentId", "TrackingNumber");
    }

    [Theory]
    [InlineData("A",new[] { 104, 33, 34, 106 })]
    [InlineData("123456", new[] { 104, 17, 18, 19, 20, 21, 22, 16, 106 })]
    public void Code128_b_uses_standard_start_checksum_and_stop(string value, int[] expected) =>
        Code128Barcode.Encode(value).Should().Equal(expected);

    [Fact]
    public void Barcode_rejects_unencodable_characters()
    {
        var action = () => Code128Barcode.Encode("é");
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Tracking_numbers_are_unique_uppercase_ascii_with_the_expected_prefix()
    {
        var generator = new TrackingNumberGenerator();
        var values = Enumerable.Range(0, 1000).Select(_ => generator.Generate()).ToArray();
        values.Should().OnlyHaveUniqueItems();
        values.Should().AllSatisfy(value => value.Should().MatchRegex("^PF[0-9A-F]{24}$"));
    }

    [Fact]
    public void Label_is_a_complete_binary_pdf_with_an_embedded_unicode_font()
    {
        var pdf = new ShippingLabelGenerator().Generate(CreateShipment("Élodie François"));
        Encoding.ASCII.GetString(pdf.AsSpan(0, 8)).Should().Be("%PDF-1.7");
        var structure = Encoding.ASCII.GetString(pdf);
        structure.Should().Contain("/FontFile2").And.Contain("/ToUnicode").And.Contain("xref\n").And.EndWith("%%EOF\n");
    }

    [Fact]
    public void Label_fails_explicitly_for_missing_font_glyphs_instead_of_printing_an_incorrect_address()
    {
        var action = () => new ShippingLabelGenerator().Generate(CreateShipment("Client 😀"));
        action.Should().Throw<ValidationException>();
    }

    private static ParcelShipment CreateShipment(string name)
    {
        var sender = new ShippingAddress("Emma Martin", "emma@example.test", "+33600000001", "12 rue des Peupliers", null, "69003", "Lyon", "FR");
        var recipient = new ShippingAddress(name, "elodie@example.test", "+33600000002", "18 allée des Érables", "Bâtiment B", "75012", "Paris", "FR");
        return new ParcelShipment("customer-one", "PF0123456789ABCDEF01234567", sender, recipient,
            new ParcelSpecification(2.5m, 30m, 20m, 10m), 8.50m, DeliveryZone.Domestic);
    }
}
