using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using ParcelFlow.BuildingBlocks.Api.Observability;
using Xunit;

namespace ParcelFlow.BuildingBlocks.Tests;

public sealed class ObservabilityTests
{
    private static IConfiguration Settings(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(x => x.Key, x => x.Value)).Build();

    [Fact]
    public void Source_names_match_the_ones_masstransit_emits()
    {
        ObservabilityExtensions.MassTransitSource.Should().Be(MassTransit.Logging.DiagnosticHeaders.DefaultListenerName);
        ObservabilityExtensions.MassTransitSource.Should().Be(MassTransit.Monitoring.InstrumentationOptions.MeterName);
    }

    [Fact]
    public void Traces_listen_to_masstransit_and_npgsql()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddParcelFlowTelemetry(Settings(), "parcelflow-test");
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<TracerProvider>();

        using var masstransit = new ActivitySource(ObservabilityExtensions.MassTransitSource);
        using var npgsql = new ActivitySource(ObservabilityExtensions.NpgsqlSource);
        using var unrelated = new ActivitySource("parcelflow-unrelated-" + Guid.NewGuid().ToString("N"));
        masstransit.HasListeners().Should().BeTrue();
        npgsql.HasListeners().Should().BeTrue();
        unrelated.HasListeners().Should().BeFalse();
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "  ", null)]
    [InlineData("http://jaeger:4317", null, "http://jaeger:4317/")]
    [InlineData(null, "http://collector:4317", "http://collector:4317/")]
    [InlineData("https://otel.example.test", "http://ignored:4317", "https://otel.example.test/")]
    public void Otlp_endpoint_prefers_the_parcelflow_key_and_falls_back_to_the_standard_variable(string? parcelflow, string? standard, string? expected)
    {
        var endpoint = ObservabilityExtensions.OtlpEndpoint(Settings(("OpenTelemetry:Endpoint", parcelflow), ("OTEL_EXPORTER_OTLP_ENDPOINT", standard)));
        endpoint?.ToString().Should().Be(expected);
        if (expected is null) endpoint.Should().BeNull();
    }

    [Theory]
    [InlineData("jaeger:4317")]
    [InlineData("ftp://jaeger:4317")]
    public void Invalid_otlp_endpoint_is_rejected(string value) =>
        FluentActions.Invoking(() => ObservabilityExtensions.OtlpEndpoint(Settings(("OpenTelemetry:Endpoint", value))))
            .Should().Throw<InvalidOperationException>();
}
