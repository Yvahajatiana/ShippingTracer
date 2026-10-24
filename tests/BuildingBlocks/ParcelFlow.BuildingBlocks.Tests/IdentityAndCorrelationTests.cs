using FluentAssertions;
using ParcelFlow.BuildingBlocks.Application.Correlation;
using ParcelFlow.BuildingBlocks.Application.Identity;
using Xunit;

namespace ParcelFlow.BuildingBlocks.Tests;

public sealed class IdentityAndCorrelationTests
{
    private static readonly Guid Source = Guid.Parse("4f8c2a51-0d3e-4b7a-9c1f-6e2d8a7b5c34");
    private static readonly Guid Other = Guid.Parse("a1b2c3d4-e5f6-4789-8abc-def012345678");

    [Fact]
    public void Deterministic_guid_is_stable_and_a_version_8_rfc_uuid()
    {
        var first = DeterministicGuid.Create("test:fact:", Source);

        DeterministicGuid.Create("test:fact:", Source).Should().Be(first);
        first.ToString("D")[14].Should().Be('8');
        "89ab".Should().Contain(first.ToString("D")[19].ToString());
    }

    [Fact]
    public void Deterministic_guid_separates_scopes_sources_and_their_order()
    {
        var values = new[]
        {
            DeterministicGuid.Create("test:fact:", Source),
            DeterministicGuid.Create("test:other-fact:", Source),
            DeterministicGuid.Create("test:fact:", Other),
            DeterministicGuid.Create("test:fact:", Source, Other),
            DeterministicGuid.Create("test:fact:", Other, Source)
        };

        values.Should().OnlyHaveUniqueItems();
        values.Should().NotContain(Source);
    }

    [Fact]
    public void Deterministic_guid_rejects_missing_inputs()
    {
        FluentActions.Invoking(() => DeterministicGuid.Create("test:fact:", Guid.Empty)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => DeterministicGuid.Create(" ", Source)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Correlation_is_generated_once_per_operation_when_nothing_was_received()
    {
        var context = new CorrelationContext();

        var generated = context.CorrelationId;

        generated.Should().NotBeEmpty();
        context.CorrelationId.Should().Be(generated);
    }

    [Fact]
    public void Received_correlation_wins_and_must_not_be_empty()
    {
        CorrelationContext.From(Source).CorrelationId.Should().Be(Source);
        FluentActions.Invoking(() => new CorrelationContext().Set(Guid.Empty)).Should().Throw<ArgumentException>();
    }
}
