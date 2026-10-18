using System.Text.Json;
using FluentAssertions;
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;
using Xunit;

namespace ParcelFlow.Billing.ContractTests;

public sealed class BillingEventContractTests
{
    [Fact]
    public void Durable_payment_command_from_shipment_keeps_its_owner_and_exact_amount()
    {
        const string json = """
        {"EventId":"11111111-1111-1111-1111-111111111111","CorrelationId":"22222222-2222-2222-2222-222222222222","OccurredAtUtc":"2026-10-18T12:00:00+00:00","ShipmentId":"22222222-2222-2222-2222-222222222222","CustomerSubject":"demo-customer","Amount":7.30,"Currency":"EUR"}
        """;
        var value = JsonSerializer.Deserialize<PaymentRequested>(json)!;
        value.CustomerSubject.Should().Be("demo-customer");
        value.Amount.Should().Be(7.30m);
        value.Currency.Should().Be("EUR");
        JsonSerializer.Deserialize<PaymentRequested>(JsonSerializer.Serialize(value)).Should().Be(value);
    }

    [Fact]
    public void Payment_result_contract_is_compatible_with_the_existing_booking_saga()
    {
        var id = Guid.NewGuid();
        var paid = new PaymentCompleted(Guid.NewGuid(), id, DateTimeOffset.UtcNow, id, 7.30m, "EUR");
        var rejected = new PaymentFailed(Guid.NewGuid(), id, paid.OccurredAtUtc, id, "Mock payment declined.");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(paid));
        document.RootElement.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(
            "EventId", "CorrelationId", "OccurredAtUtc", "ShipmentId", "Amount", "Currency");
        JsonSerializer.Deserialize<PaymentCompleted>(document.RootElement.GetRawText()).Should().Be(paid);
        JsonSerializer.Deserialize<PaymentFailed>(JsonSerializer.Serialize(rejected)).Should().Be(rejected);
    }

    [Fact]
    public void Invoice_fact_exposes_only_required_billing_identity_and_money()
    {
        var id = Guid.NewGuid();
        var value = new InvoiceIssued(Guid.NewGuid(), id, DateTimeOffset.UtcNow, id, id, "demo-customer", 7.30m, "EUR");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        document.RootElement.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(
            "EventId", "CorrelationId", "OccurredAtUtc", "InvoiceId", "ShipmentId", "CustomerSubject", "Amount", "Currency");
        JsonSerializer.Deserialize<InvoiceIssued>(document.RootElement.GetRawText()).Should().Be(value);
    }

    [Fact]
    public void Credit_fact_roundtrips_without_actor_or_internal_reason()
    {
        var id = Guid.NewGuid();
        var creditId = Guid.NewGuid();
        var value = new CreditNoteIssued(Guid.NewGuid(), id, DateTimeOffset.UtcNow, creditId, id, id, "demo-customer", 2m, "EUR");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        document.RootElement.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(
            "EventId", "CorrelationId", "OccurredAtUtc", "CreditNoteId", "InvoiceId", "ShipmentId", "CustomerSubject", "Amount", "Currency");
        JsonSerializer.Deserialize<CreditNoteIssued>(document.RootElement.GetRawText()).Should().Be(value);
    }
}
