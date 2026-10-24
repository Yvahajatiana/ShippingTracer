using System.Collections.Concurrent;
using FluentAssertions;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.BuildingBlocks.Application.Identity;
using ParcelFlow.BuildingBlocks.Contracts.Reporting.V1;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Reporting.Application;
using ParcelFlow.Reporting.Infrastructure.Persistence;
using Xunit;
namespace ParcelFlow.Reporting.IntegrationTests;
[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceConsumerTests(InfrastructureFixture infrastructure)
{
    private IBusControl Observer(Action<IRabbitMqReceiveEndpointConfigurator> configure) => Bus.Factory.CreateUsingRabbitMq(rabbit =>
    {
        rabbit.Host(infrastructure.RabbitMq.Hostname, infrastructure.RabbitMq.GetMappedPublicPort(5672), "/", host => { host.Username("rabbitmq"); host.Password("rabbitmq"); });
        rabbit.ReceiveEndpoint("reporting-test-observer-" + Guid.NewGuid().ToString("N"), endpoint => { endpoint.AutoDelete = true; configure(endpoint); });
    });
    [Fact]
    public async Task Twenty_concurrent_redeliveries_and_fresh_envelope_for_same_source_emit_one_projection_event()
    {
        await using var provider = ConsumerTestHost.Create(infrastructure); await provider.InitializeReportingDatabaseAsync();
        var outputs = new ConcurrentBag<ShipmentMetricsUpdated>();
        var observer = Observer(endpoint => endpoint.Handler<ShipmentMetricsUpdated>(context => { outputs.Add(context.Message); return Task.CompletedTask; }));
        var bus = provider.GetRequiredService<IBusControl>(); await observer.StartAsync(); await bus.StartAsync();
        try
        {
            var shipment = Guid.NewGuid(); var command = ConsumerTestHost.Command(shipment, Domain.ParcelStatus.Created, DateTimeOffset.UtcNow.AddTicks(7));
            var message = new TrackingStatusChanged(command.EventId, shipment, command.OccurredAtUtc, shipment, command.TrackingNumber, TrackingStatus.Created, null, command.SourceEventId);
            var transportIds = Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToArray();
            await Task.WhenAll(transportIds.Take(20).Select(id => bus.Publish(message, context => context.MessageId = id)));
            await ConsumerTestHost.Until(() => Task.FromResult(outputs.Count(x => x.SourceEventId == command.SourceEventId) == 1));
            var replayEventId = Guid.NewGuid();
            await bus.Publish(message with { EventId = replayEventId }, context => context.MessageId = transportIds[20]);
            await ConsumerTestHost.Until(async () => { using var scope = provider.CreateScope(); return await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Set<InboxState>().CountAsync(x => transportIds.Contains(x.MessageId) && x.Consumed != null) == 21; });
            using var read = provider.CreateScope(); var db = read.ServiceProvider.GetRequiredService<ReportingDbContext>();
            (await db.Facts.CountAsync(x => x.ShipmentId == shipment)).Should().Be(1);
            (await db.Shipments.SingleAsync(x => x.ShipmentId == shipment)).Revision.Should().Be(1);
            var replayFact = await read.ServiceProvider.GetRequiredService<IReportingStore>().FindFactByEventIdAsync(replayEventId, default);
            replayFact.Should().NotBeNull();
            replayFact!.SourceEventId.Should().Be(command.SourceEventId);
            outputs.Count(x => x.SourceEventId == command.SourceEventId).Should().Be(1);
        }
        finally { await bus.StopAsync(); await observer.StopAsync(); }
    }
    [Fact]
    public async Task Projection_event_propagates_the_consumed_correlation_and_uses_its_event_id_as_message_id()
    {
        await using var provider = ConsumerTestHost.Create(infrastructure); await provider.InitializeReportingDatabaseAsync();
        var outputs = new ConcurrentBag<ConsumeContext<ShipmentMetricsUpdated>>();
        var observer = Observer(endpoint => endpoint.Handler<ShipmentMetricsUpdated>(context => { outputs.Add(context); return Task.CompletedTask; }));
        var bus = provider.GetRequiredService<IBusControl>(); await observer.StartAsync(); await bus.StartAsync();
        try
        {
            var shipment = Guid.NewGuid(); var flow = Guid.NewGuid(); var source = Guid.NewGuid();
            var tracking = "PF" + shipment.ToString("N")[..24].ToUpperInvariant();
            // The flow correlation differs from the shipment identifier: Reporting must keep it, not replace it.
            await bus.Publish(new TrackingStatusChanged(Guid.NewGuid(), flow, DateTimeOffset.UtcNow, shipment, tracking, TrackingStatus.Created, null, source));
            await ConsumerTestHost.Until(() => Task.FromResult(outputs.Any(x => x.Message.SourceEventId == source)));
            var output = outputs.Single(x => x.Message.SourceEventId == source);
            output.Message.CorrelationId.Should().Be(flow);
            output.CorrelationId.Should().Be(flow);
            output.Message.EventId.Should().Be(DeterministicGuid.Create("reporting:metrics-updated:", source));
            output.MessageId.Should().Be(output.Message.EventId);
        }
        finally { await bus.StopAsync(); await observer.StopAsync(); }
    }
    [Theory]
    [InlineData(TrackingStatus.Created, Domain.ParcelStatus.Created)]
    [InlineData(TrackingStatus.PickedUp, Domain.ParcelStatus.PickedUp)]
    [InlineData(TrackingStatus.InTransit, Domain.ParcelStatus.InTransit)]
    [InlineData(TrackingStatus.AtSortingCenter, Domain.ParcelStatus.AtSortingCenter)]
    [InlineData(TrackingStatus.OutForDelivery, Domain.ParcelStatus.OutForDelivery)]
    [InlineData(TrackingStatus.Delivered, Domain.ParcelStatus.Delivered)]
    [InlineData(TrackingStatus.Failed, Domain.ParcelStatus.Failed)]
    [InlineData(TrackingStatus.Returned, Domain.ParcelStatus.Returned)]
    public async Task Consumer_maps_each_contract_status_exactly(TrackingStatus status, Domain.ParcelStatus expected)
    {
        await using var provider = ConsumerTestHost.Create(infrastructure); await provider.InitializeReportingDatabaseAsync();
        var bus = provider.GetRequiredService<IBusControl>(); await bus.StartAsync();
        try
        {
            var id = Guid.NewGuid(); var source = Guid.NewGuid(); var tracking = "PF" + id.ToString("N")[..24].ToUpperInvariant();
            await bus.Publish(new TrackingStatusChanged(Guid.NewGuid(), id, DateTimeOffset.UtcNow, id, tracking, status, "CDG-01", source));
            await ConsumerTestHost.Until(async () => { using var scope = provider.CreateScope(); return await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Facts.AnyAsync(x => x.SourceEventId == source); });
            using var read = provider.CreateScope();
            (await read.ServiceProvider.GetRequiredService<ReportingDbContext>().Facts.SingleAsync(x => x.SourceEventId == source)).Status.Should().Be(expected);
        }
        finally { await bus.StopAsync(); }
    }
    [Fact]
    public async Task Unknown_status_and_conflicting_source_fault_without_persisting_invalid_facts()
    {
        await using var provider = ConsumerTestHost.Create(infrastructure); await provider.InitializeReportingDatabaseAsync();
        var failures = new ConcurrentBag<Fault<TrackingStatusChanged>>();
        var observer = Observer(endpoint => endpoint.Handler<Fault<TrackingStatusChanged>>(context => { failures.Add(context.Message); return Task.CompletedTask; }));
        var bus = provider.GetRequiredService<IBusControl>(); await observer.StartAsync(); await bus.StartAsync();
        try
        {
            var original = ConsumerTestHost.Command(Guid.NewGuid(), Domain.ParcelStatus.Created, DateTimeOffset.UtcNow);
            using (var scope = provider.CreateScope()) await scope.ServiceProvider.GetRequiredService<ISender>().Send(original);
            var conflictId = Guid.NewGuid(); var invalidId = Guid.NewGuid();
            await bus.Publish(new TrackingStatusChanged(conflictId, original.ShipmentId, original.OccurredAtUtc, original.ShipmentId, original.TrackingNumber, TrackingStatus.Delivered, null, original.SourceEventId));
            await bus.Publish(new TrackingStatusChanged(invalidId, original.ShipmentId, original.OccurredAtUtc, original.ShipmentId, original.TrackingNumber, (TrackingStatus)999, null, Guid.NewGuid()));
            await ConsumerTestHost.Until(() => Task.FromResult(failures.Any(x => x.Message.EventId == conflictId) && failures.Any(x => x.Message.EventId == invalidId)));
            using var read = provider.CreateScope(); var db = read.ServiceProvider.GetRequiredService<ReportingDbContext>();
            (await db.Facts.CountAsync(x => x.ShipmentId == original.ShipmentId)).Should().Be(1);
            failures.Single(x => x.Message.EventId == conflictId).Exceptions.Should().Contain(x => x.ExceptionType.Contains(nameof(ReportingConflictException)));
        }
        finally { await bus.StopAsync(); await observer.StopAsync(); }
    }
}
