using System.Text.Json;
using FluentAssertions;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using Xunit;

namespace ParcelFlow.Warehouse.ContractTests;

public sealed class WarehouseEventContractTests
{
    [Fact]
    public void Ready_event_from_shipment_preserves_the_registration_contract()
    {
        const string json = """
        {"EventId":"11111111-1111-1111-1111-111111111111","CorrelationId":"22222222-2222-2222-2222-222222222222","OccurredAtUtc":"2026-10-17T12:00:00.123456+00:00","ShipmentId":"22222222-2222-2222-2222-222222222222","TrackingNumber":"PF0123456789ABCDEF01234567"}
        """;
        var message = JsonSerializer.Deserialize<ShipmentReadyForPickup>(json)!;
        message.ShipmentId.Should().Be(message.CorrelationId);
        message.TrackingNumber.Should().Be("PF0123456789ABCDEF01234567");
        message.OccurredAtUtc.Ticks.Should().Be(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero).AddTicks(1234560).Ticks);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(message));
        document.RootElement.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(
            "EventId", "CorrelationId", "OccurredAtUtc", "ShipmentId", "TrackingNumber");
    }

    [Fact]
    public void Pickup_event_for_shipment_and_tracking_contains_only_logistics_identity()
    {
        const string json = """
        {"EventId":"11111111-1111-1111-1111-111111111111","CorrelationId":"22222222-2222-2222-2222-222222222222","OccurredAtUtc":"2026-10-17T12:00:00+00:00","ShipmentId":"22222222-2222-2222-2222-222222222222"}
        """;
        var message = JsonSerializer.Deserialize<ParcelPickedUp>(json)!;
        message.ShipmentId.Should().Be(message.CorrelationId);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(message));
        document.RootElement.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(
            "EventId", "CorrelationId", "OccurredAtUtc", "ShipmentId");
        JsonSerializer.Deserialize<ParcelPickedUp>(JsonSerializer.Serialize(message)).Should().Be(message);
    }
}
