using System.Text.Json;
using FluentAssertions;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using Xunit;

namespace ParcelFlow.Tracking.ContractTests;

public sealed class TrackingEventContractTests
{
    [Theory]
    [InlineData(TrackingStatus.Created, "Created")]
    [InlineData(TrackingStatus.PickedUp, "PickedUp")]
    [InlineData(TrackingStatus.InTransit, "InTransit")]
    [InlineData(TrackingStatus.AtSortingCenter, "AtSortingCenter")]
    [InlineData(TrackingStatus.OutForDelivery, "OutForDelivery")]
    [InlineData(TrackingStatus.Delivered, "Delivered")]
    [InlineData(TrackingStatus.Failed, "Failed")]
    [InlineData(TrackingStatus.Returned, "Returned")]
    public void Logistics_status_names_are_stable_strings(TrackingStatus status, string name)
    {
        var id = Guid.NewGuid();
        var message = new ParcelStatusRecorded(Guid.NewGuid(), id, DateTimeOffset.UtcNow, id,
            "PF0123456789ABCDEF01234567", status, "FR-LYO-01");
        var json = JsonSerializer.Serialize(message);
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("Status").GetString().Should().Be(name);
        JsonSerializer.Deserialize<ParcelStatusRecorded>(json).Should().Be(message);
    }

    [Fact]
    public void Warehouse_delivery_wire_contract_keeps_logistics_only_fields()
    {
        const string json = """
        {"EventId":"11111111-1111-1111-1111-111111111111","CorrelationId":"22222222-2222-2222-2222-222222222222","OccurredAtUtc":"2026-10-17T12:00:00+00:00","ShipmentId":"22222222-2222-2222-2222-222222222222","TrackingNumber":"PF0123456789ABCDEF01234567","Status":"AtSortingCenter","FacilityCode":"FR-LYO-01"}
        """;
        var message = JsonSerializer.Deserialize<ParcelStatusRecorded>(json)!;
        message.ShipmentId.Should().Be(message.CorrelationId);
        message.Status.Should().Be(TrackingStatus.AtSortingCenter);
        message.FacilityCode.Should().Be("FR-LYO-01");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(message));
        document.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "EventId", "CorrelationId", "OccurredAtUtc", "ShipmentId", "TrackingNumber", "Status", "FacilityCode");
    }

    [Fact]
    public void Outgoing_tracking_event_preserves_source_identity_and_original_occurrence_time()
    {
        var shipmentId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var time = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var message = new TrackingStatusChanged(Guid.NewGuid(), shipmentId, time, shipmentId,
            "PF0123456789ABCDEF01234567", TrackingStatus.Delivered, null, sourceId);
        var result = JsonSerializer.Deserialize<TrackingStatusChanged>(JsonSerializer.Serialize(message));
        result.Should().Be(message);
        result!.SourceEventId.Should().Be(sourceId);
        result.EventId.Should().NotBe(sourceId);
        result.OccurredAtUtc.Should().Be(time);
    }

    [Fact]
    public void Unknown_status_names_are_rejected_instead_of_becoming_created()
    {
        var read = () => JsonSerializer.Deserialize<TrackingStatus>("\"UnsupportedStatus\"");
        read.Should().Throw<JsonException>();
    }
}
