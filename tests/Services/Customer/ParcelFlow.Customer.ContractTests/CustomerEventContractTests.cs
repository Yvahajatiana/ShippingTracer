using System.Text.Json;
using FluentAssertions;
using ParcelFlow.BuildingBlocks.Contracts;
using ParcelFlow.BuildingBlocks.Contracts.Customer.V1;
using Xunit;

namespace ParcelFlow.Customer.ContractTests;

public sealed class CustomerEventContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    [Fact]
    public void Created_profile_preserves_public_wire_contract()
    {
        var message = new CustomerProfileCreated(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            Guid.NewGuid(), "keycloak-subject-123");

        AssertRoundTrip(message);
        var json = JsonSerializer.SerializeToElement(message, JsonOptions);
        json.GetProperty("customerId").GetGuid().Should().Be(message.CustomerId);
        json.GetProperty("keycloakSubject").GetString().Should().Be(message.KeycloakSubject);
        json.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "eventId", "correlationId", "occurredAtUtc", "customerId", "keycloakSubject");
    }

    [Fact]
    public void Updated_profile_and_company_contracts_round_trip()
    {
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        AssertRoundTrip(new CustomerProfileUpdated(eventId, correlationId, occurredAt, Guid.NewGuid()));
        AssertRoundTrip(new CompanyCreated(eventId, correlationId, occurredAt, Guid.NewGuid()));
        AssertRoundTrip(new CompanyUpdated(eventId, correlationId, occurredAt, Guid.NewGuid()));
    }

    private static void AssertRoundTrip<T>(T message) where T : IntegrationEvent
    {
        var serialized = JsonSerializer.Serialize(message, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<T>(serialized, JsonOptions);
        deserialized.Should().Be(message);
        deserialized!.EventId.Should().NotBeEmpty();
        deserialized.CorrelationId.Should().NotBeEmpty();
        deserialized.OccurredAtUtc.Offset.Should().Be(TimeSpan.Zero);
    }
}
