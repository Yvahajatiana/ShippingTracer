using System.Text.Json;
using FluentAssertions;
using ParcelFlow.BuildingBlocks.Contracts.Reporting.V1;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using Xunit;

namespace ParcelFlow.Reporting.ContractTests;

public sealed class ReportingEventContractTests
{
    [Fact]
    public void Projection_update_exposes_identifiers_without_contacts_or_shipment_details()
    {
        var shipment = Guid.NewGuid();
        var value = new ShipmentMetricsUpdated(Guid.NewGuid(), shipment, DateTimeOffset.UtcNow, shipment, Guid.NewGuid());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(value));
        json.RootElement.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(
            "EventId", "CorrelationId", "OccurredAtUtc", "ShipmentId", "SourceEventId");
        JsonSerializer.Deserialize<ShipmentMetricsUpdated>(json.RootElement.GetRawText()).Should().Be(value);
        value.CorrelationId.Should().Be(value.ShipmentId);
    }

    [Theory]
    [InlineData("Created", TrackingStatus.Created)]
    [InlineData("Delivered", TrackingStatus.Delivered)]
    [InlineData("Returned", TrackingStatus.Returned)]
    public void Tracking_v1_fixture_preserves_original_fact_identity_and_string_status(string status, TrackingStatus expected)
    {
        var json = $$"""
        {"EventId":"11111111-1111-1111-1111-111111111111","CorrelationId":"22222222-2222-2222-2222-222222222222","OccurredAtUtc":"2026-10-18T10:00:00+00:00","ShipmentId":"22222222-2222-2222-2222-222222222222","TrackingNumber":"PF900000000000000000000001","Status":"{{status}}","FacilityCode":null,"SourceEventId":"33333333-3333-3333-3333-333333333333"}
        """;
        var value = JsonSerializer.Deserialize<TrackingStatusChanged>(json)!;
        value.Status.Should().Be(expected);
        value.SourceEventId.Should().NotBe(value.EventId);
        JsonSerializer.Deserialize<TrackingStatusChanged>(JsonSerializer.Serialize(value)).Should().Be(value);
    }
}
