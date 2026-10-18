using System.Text.Json;
using FluentAssertions;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using Xunit;

namespace ParcelFlow.Delivery.ContractTests;

public sealed class DeliveryEventContractTests
{
    [Theory]
    [InlineData("OutForDelivery", TrackingStatus.OutForDelivery)]
    [InlineData("Delivered", TrackingStatus.Delivered)]
    [InlineData("Failed", TrackingStatus.Failed)]
    public void Delivery_status_contract_remains_compatible_and_excludes_proof_or_recipient_data(string status, TrackingStatus expected)
    {
        const string fixture = """
        {"EventId":"11111111-1111-1111-1111-111111111111","CorrelationId":"22222222-2222-2222-2222-222222222222","OccurredAtUtc":"2026-10-18T12:00:00.123456+00:00","ShipmentId":"22222222-2222-2222-2222-222222222222","TrackingNumber":"PF0123456789ABCDEF01234567","Status":"OutForDelivery","FacilityCode":null}
        """;
        var message = JsonSerializer.Deserialize<ParcelStatusRecorded>(fixture.Replace("OutForDelivery", status, StringComparison.Ordinal))!;
        message.Status.Should().Be(expected);
        message.CorrelationId.Should().Be(message.ShipmentId);
        message.FacilityCode.Should().BeNull();
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(message));
        document.RootElement.GetProperty("Status").GetString().Should().Be(status);
        document.RootElement.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(
            "EventId", "CorrelationId", "OccurredAtUtc", "ShipmentId", "TrackingNumber", "Status", "FacilityCode");
        JsonSerializer.Deserialize<ParcelStatusRecorded>(JsonSerializer.Serialize(message)).Should().Be(message);
    }
}
