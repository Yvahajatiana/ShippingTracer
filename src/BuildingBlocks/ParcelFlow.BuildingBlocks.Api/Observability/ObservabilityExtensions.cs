using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace ParcelFlow.BuildingBlocks.Api.Observability;

public static class ObservabilityExtensions
{
    /// <summary>ActivitySource and Meter of MassTransit (DiagnosticHeaders.DefaultListenerName, InstrumentationOptions.MeterName).</summary>
    public const string MassTransitSource = "MassTransit";
    /// <summary>ActivitySource of Npgsql 8: one span per database command.</summary>
    public const string NpgsqlSource = "Npgsql";
    /// <summary>Console template: the trace and span identifiers lead to the trace in Jaeger, the correlation to the business flow.</summary>
    public const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} trace={TraceId} span={SpanId} correlation={CorrelationId}{NewLine}{Exception}";

    /// <summary>
    /// Shared logging and telemetry of the gateway and the services: Serilog to the console, OpenTelemetry
    /// traces (ASP.NET Core, HttpClient, MassTransit, Npgsql) and metrics (Prometheus scraping endpoint),
    /// exported over OTLP only when an endpoint is configured.
    /// </summary>
    public static WebApplicationBuilder AddParcelFlowObservability(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Host.UseSerilog((context, logging) => logging
            .ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: ConsoleTemplate, formatProvider: System.Globalization.CultureInfo.InvariantCulture));
        builder.Services.AddParcelFlowTelemetry(builder.Configuration, serviceName);
        return builder;
    }

    public static IServiceCollection AddParcelFlowTelemetry(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        var endpoint = OtlpEndpoint(configuration);
        services.AddOpenTelemetry().ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddSource(MassTransitSource, NpgsqlSource);
                if (endpoint is not null) tracing.AddOtlpExporter(options => options.Endpoint = endpoint);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddMeter(MassTransitSource).AddPrometheusExporter();
                if (endpoint is not null) metrics.AddOtlpExporter(options => options.Endpoint = endpoint);
            });
        return services;
    }

    /// <summary><c>OpenTelemetry:Endpoint</c>, else the standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>; null disables the export.</summary>
    public static Uri? OtlpEndpoint(IConfiguration configuration)
    {
        var value = configuration["OpenTelemetry:Endpoint"];
        if (string.IsNullOrWhiteSpace(value)) value = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri : throw new InvalidOperationException("OpenTelemetry:Endpoint must be an absolute http(s) URL.");
    }
}
