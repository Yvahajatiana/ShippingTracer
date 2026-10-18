using System.Text.Json;
using FluentAssertions;
using ParcelFlow.BuildingBlocks.Contracts.Notification.V1;
using Xunit;

namespace ParcelFlow.Notification.ContractTests;

public sealed class NotificationEventContractTests
{
    [Theory]
    [InlineData("Email")]
    [InlineData("Sms")]
    public void Mock_receipt_preserves_correlation_without_exposing_contact_or_content(string channel)
    {
        var shipmentId = Guid.NewGuid();
        var value = new NotificationRecorded(Guid.NewGuid(), shipmentId,
            DateTimeOffset.Parse("2026-10-18T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            Guid.NewGuid(), Guid.NewGuid(), shipmentId, "demo-customer", channel);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(value));
        json.RootElement.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(
            "EventId", "CorrelationId", "OccurredAtUtc", "NotificationId", "SourceEventId", "ShipmentId", "CustomerSubject", "Channel");
        JsonSerializer.Deserialize<NotificationRecorded>(json.RootElement.GetRawText()).Should().Be(value);
        value.CorrelationId.Should().Be(value.ShipmentId);
        value.EventId.Should().NotBe(value.SourceEventId);
    }

    [Fact]
    public void V1_receipt_fixture_deserializes_with_distinct_source_and_receipt_identifiers()
    {
        const string json = """
        {"EventId":"11111111-1111-1111-1111-111111111111","CorrelationId":"22222222-2222-2222-2222-222222222222","OccurredAtUtc":"2026-10-18T10:00:00+00:00","NotificationId":"33333333-3333-3333-3333-333333333333","SourceEventId":"44444444-4444-4444-4444-444444444444","ShipmentId":"22222222-2222-2222-2222-222222222222","CustomerSubject":"demo-customer","Channel":"Email"}
        """;
        var value = JsonSerializer.Deserialize<NotificationRecorded>(json)!;
        value.Channel.Should().Be("Email");
        value.NotificationId.Should().NotBe(value.SourceEventId);
        value.CustomerSubject.Should().Be("demo-customer");
        JsonSerializer.Deserialize<NotificationRecorded>(JsonSerializer.Serialize(value)).Should().Be(value);
    }
}
