extern alias TrackingApi;

using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Reporting.Application;
using ParcelFlow.Reporting.Domain;
using ParcelFlow.Reporting.Infrastructure;
using ParcelFlow.Reporting.Infrastructure.Persistence;
using ParcelFlow.Tracking.Infrastructure;
using ParcelFlow.Tracking.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Reporting.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class WorkflowTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Real_tracking_outbox_drives_kpis_despite_late_creation_pickup_and_post_delivery_facts()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await using var source = new TrackingFactory(infrastructure, Connection("reporting_tracking_" + suffix));
        await source.Services.InitializeTrackingDatabaseAsync();
        using var target = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(Settings(infrastructure, "ReportingDatabase", Connection("reporting_workflow_" + suffix))))
            .ConfigureServices((context, services) => services.AddReportingApplication().AddReportingInfrastructure(context.Configuration)).Build();
        await target.Services.InitializeReportingDatabaseAsync();
        await target.StartAsync();
        try
        {
            var shipment = Guid.NewGuid();
            var tracking = "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
            var bus = source.Services.GetRequiredService<IBus>();
            var createdAt = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
            var deliveredAt = createdAt.AddHours(26);
            await bus.Publish(new ParcelStatusRecorded(Guid.NewGuid(), shipment, deliveredAt, shipment, tracking, TrackingStatus.Delivered, null));
            await WaitForProjection(target, shipment, 1, ParcelStatus.Delivered);
            (await Report(target, 10)).TotalShipments.Should().Be(0, "an unknown creation date cannot be assigned to a creation cohort");

            await bus.Publish(new ShipmentCreated(Guid.NewGuid(), shipment, createdAt, shipment, tracking, "reporting-workflow-customer", 7.30m, "EUR"));
            await WaitForProjection(target, shipment, 2, ParcelStatus.Delivered);
            var afterCreation = await Report(target, 11);
            afterCreation.TotalShipments.Should().Be(1);
            afterCreation.DeliveredShipments.Should().Be(1);
            afterCreation.AverageDeliveryHours.Should().Be(26m);
            afterCreation.AverageTransitHours.Should().BeNull();

            await bus.Publish(new ParcelPickedUp(Guid.NewGuid(), shipment, createdAt.AddHours(2), shipment));
            await WaitForProjection(target, shipment, 3, ParcelStatus.Delivered);
            var afterPickup = await Report(target, 12);
            afterPickup.DeliveryRatePercent.Should().Be(100m);
            afterPickup.AverageTransitHours.Should().Be(24m);
            afterPickup.TransitDurationSamples.Should().Be(1);

            await bus.Publish(new ParcelStatusRecorded(Guid.NewGuid(), shipment, deliveredAt.AddHours(1), shipment, tracking, TrackingStatus.Failed, null));
            await WaitForProjection(target, shipment, 4, ParcelStatus.Delivered);
            (await Report(target, 13)).DeliveredShipments.Should().Be(1, "Tracking preserves its delivered terminal state after a late failure");

            await bus.Publish(new ParcelStatusRecorded(Guid.NewGuid(), shipment, deliveredAt.AddDays(1), shipment, tracking, TrackingStatus.Returned, "FR-LYON-01"));
            await WaitForProjection(target, shipment, 5, ParcelStatus.Returned);
            var returned = await Report(target, 14);
            returned.TotalShipments.Should().Be(1);
            returned.DeliveredShipments.Should().Be(0);
            returned.DeliveryRatePercent.Should().Be(0m);
            returned.AverageDeliveryHours.Should().BeNull();
            returned.DeliveryDurationSamples.Should().Be(0);
            returned.AverageTransitHours.Should().BeNull();
            returned.StatusVolumes.Single(x => x.Status == ParcelStatus.Returned).Shipments.Should().Be(1);
            returned.DailyVolumes.Sum(x => x.Shipments).Should().Be(1);
            using var client = source.CreateClient();
            using var json = JsonDocument.Parse(await client.GetStringAsync("/api/v1/tracking/" + tracking));
            json.RootElement.GetProperty("currentStatus").GetString().Should().Be(ParcelStatus.Returned.ToString());
        }
        finally { await target.StopAsync(); }
    }

    private static async Task<KpiReportDto> Report(IHost target, int startDay)
    {
        await using var scope = target.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetKpis(new DateOnly(2026, 10, startDay), new DateOnly(2026, 11, 1)));
    }
    private static async Task WaitForProjection(IHost target, Guid shipment, int facts, ParcelStatus status)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        while (true)
        {
            await using var scope = target.Services.CreateAsyncScope();
            var root = await scope.ServiceProvider.GetRequiredService<IReportingStore>().FindShipmentAsync(shipment, deadline.Token);
            if (root?.Facts.Count == facts && root.CurrentStatus == status) return;
            await Task.Delay(100, deadline.Token);
        }
    }
    private string Connection(string database) => new NpgsqlConnectionStringBuilder(infrastructure.Postgres.GetConnectionString()) { Database = database }.ConnectionString;
    private static Dictionary<string, string?> Settings(InfrastructureFixture dependencies, string key, string connection) => new()
    {
        [$"ConnectionStrings:{key}"] = connection,
        ["ConnectionStrings:Redis"] = dependencies.Redis.GetConnectionString(),
        ["RabbitMq:Host"] = dependencies.RabbitMq.Hostname,
        ["RabbitMq:Port"] = dependencies.RabbitMq.GetMappedPublicPort(5672).ToString(CultureInfo.InvariantCulture),
        ["RabbitMq:Username"] = "rabbitmq", ["RabbitMq:Password"] = "rabbitmq",
        ["Database:Initialize"] = "false", ["Database:SeedDemoData"] = "false"
    };
    private sealed class TrackingFactory(InfrastructureFixture dependencies, string connection) : WebApplicationFactory<TrackingApi::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(Settings(dependencies, "TrackingDatabase", connection)));
            // Testing enforces HTTPS metadata; this anonymous request never contacts the issuer.
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = "https://issuer.test/realms/parcelflow",
                ["Keycloak:Audience"] = "tracking-api"
            }));
        }
    }
}
