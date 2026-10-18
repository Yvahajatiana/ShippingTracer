using System.Collections.Concurrent;
using FluentAssertions;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;
using ParcelFlow.BuildingBlocks.Contracts.Notification.V1;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Notification.Application;
using ParcelFlow.Notification.Infrastructure;
using ParcelFlow.Notification.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Notification.IntegrationTests;

internal sealed class FixedRecipientResolver : IShipmentRecipientResolver
{
    public const string Owner = "notification-owner";
    public const string Tracking = "PF800000000000000000000001";
    private int _calls;
    public int Calls => _calls;
    public Task<ShipmentRecipient> ResolveAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(new ShipmentRecipient(Owner, Tracking, "owner@example.test", "+33600000001"));
    }
}
internal static class ConsumerTestHost
{
    public static ServiceProvider Create(InfrastructureFixture infrastructure, IShipmentRecipientResolver resolver, Action<IServiceCollection>? configure = null)
    {
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:NotificationDatabase"] = infrastructure.Postgres.GetConnectionString(),
            ["ConnectionStrings:Redis"] = infrastructure.Redis.GetConnectionString(),
            ["RabbitMq:Host"] = infrastructure.RabbitMq.Hostname,
            ["RabbitMq:Port"] = infrastructure.RabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:Username"] = "rabbitmq", ["RabbitMq:Password"] = "rabbitmq"
        }).Build();
        var services = new ServiceCollection(); services.AddSingleton<IConfiguration>(settings); services.AddLogging();
        services.AddNotificationApplication(); services.AddNotificationInfrastructure(settings);
        services.RemoveAll<IShipmentRecipientResolver>(); services.AddSingleton(resolver);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
    public static async Task Until(Func<Task<bool>> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!await condition()) await Task.Delay(50, deadline.Token);
    }
}
[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceConsumerTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Six_event_types_produce_private_receipts_and_public_outgoing_metadata_while_created_tracking_is_suppressed()
    {
        var resolver = new FixedRecipientResolver(); await using var provider = ConsumerTestHost.Create(infrastructure, resolver);
        await provider.InitializeNotificationDatabaseAsync(); var bus = provider.GetRequiredService<IBusControl>(); await bus.StartAsync();
        try
        {
            var shipment = Guid.NewGuid(); var at = DateTimeOffset.UtcNow.AddTicks(7); var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
            await bus.Publish(new ShipmentCreated(ids[0], shipment, at, shipment, FixedRecipientResolver.Tracking, FixedRecipientResolver.Owner, 12m, "EUR"));
            await bus.Publish(new TrackingStatusChanged(ids[1], shipment, at, shipment, FixedRecipientResolver.Tracking, TrackingStatus.Delivered, null, Guid.NewGuid()));
            await bus.Publish(new InvoiceIssued(ids[2], shipment, at, shipment, shipment, FixedRecipientResolver.Owner, 12m, "EUR"));
            await bus.Publish(new PaymentCompleted(ids[3], shipment, at, shipment, 12m, "EUR"));
            await bus.Publish(new PaymentFailed(ids[4], shipment, at, shipment, "Sensitive provider reason is never copied"));
            await bus.Publish(new CreditNoteIssued(ids[5], shipment, at, Guid.NewGuid(), shipment, shipment, FixedRecipientResolver.Owner, 2m, "EUR"));
            await bus.Publish(new TrackingStatusChanged(Guid.NewGuid(), shipment, at, shipment, FixedRecipientResolver.Tracking, TrackingStatus.Created, null, Guid.NewGuid()));
            await ConsumerTestHost.Until(async () => { using var scope = provider.CreateScope(); return await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Messages.CountAsync(x => x.ShipmentId == shipment) == 12; });
            using var read = provider.CreateScope(); var db = read.ServiceProvider.GetRequiredService<NotificationDbContext>();
            (await db.Events.CountAsync(x => x.ShipmentId == shipment)).Should().Be(6);
            (await db.Messages.Where(x => x.ShipmentId == shipment).ToListAsync()).Should().OnlyContain(x => x.DeliveryStatus == "MockSent" && !x.Body.Contains("Sensitive"));
            resolver.Calls.Should().Be(6);
        }
        finally { await bus.StopAsync(); }
    }
    [Fact]
    public async Task Twenty_concurrent_redeliveries_create_two_receipts_and_two_outgoing_events_with_one_contact_lookup()
    {
        var resolver = new FixedRecipientResolver(); await using var provider = ConsumerTestHost.Create(infrastructure, resolver); await provider.InitializeNotificationDatabaseAsync();
        var output = new ConcurrentBag<NotificationRecorded>();
        var observer = Bus.Factory.CreateUsingRabbitMq(rabbit =>
        {
            rabbit.Host(infrastructure.RabbitMq.Hostname, infrastructure.RabbitMq.GetMappedPublicPort(5672), "/", host => { host.Username("rabbitmq"); host.Password("rabbitmq"); });
            rabbit.ReceiveEndpoint("notification-test-observer-" + Guid.NewGuid().ToString("N"), endpoint => { endpoint.AutoDelete = true; endpoint.Handler<NotificationRecorded>(context => { output.Add(context.Message); return Task.CompletedTask; }); });
        });
        var bus = provider.GetRequiredService<IBusControl>(); await observer.StartAsync(); await bus.StartAsync();
        try
        {
            var shipment = Guid.NewGuid(); var eventId = Guid.NewGuid(); var at = DateTimeOffset.UtcNow.AddTicks(7);
            var item = new ShipmentCreated(eventId, shipment, at, shipment, FixedRecipientResolver.Tracking, FixedRecipientResolver.Owner, 10m, "EUR");
            var transportIds = Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToArray();
            await Task.WhenAll(transportIds.Take(20).Select(id => bus.Publish(item, context => context.MessageId = id)));
            await ConsumerTestHost.Until(() => Task.FromResult(output.Count(x => x.SourceEventId == eventId) == 2));
            // A fresh transport ID also proves deduplication against the persisted microsecond-normalized timestamp.
            await bus.Publish(item, context => context.MessageId = transportIds[20]);
            await ConsumerTestHost.Until(async () => { using var scope = provider.CreateScope(); return await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Set<InboxState>().CountAsync(x => transportIds.Contains(x.MessageId) && x.Consumed != null) == 21; });
            using var read = provider.CreateScope(); var db = read.ServiceProvider.GetRequiredService<NotificationDbContext>();
            (await db.Messages.CountAsync(x => x.SourceEventId == eventId)).Should().Be(2); output.Count(x => x.SourceEventId == eventId).Should().Be(2); resolver.Calls.Should().Be(1);
        }
        finally { await bus.StopAsync(); await observer.StopAsync(); }
    }
    [Fact]
    public async Task Conflicting_source_payload_is_rejected_without_overwriting_or_resolving_recipient_again()
    {
        var resolver = new FixedRecipientResolver(); await using var provider = ConsumerTestHost.Create(infrastructure, resolver); await provider.InitializeNotificationDatabaseAsync();
        var command = new RecordNotification(Guid.NewGuid(), Guid.NewGuid(), Domain.NotificationKind.ShipmentCreated, DateTimeOffset.UtcNow,
            FixedRecipientResolver.Owner, FixedRecipientResolver.Tracking, null, null, null, null, null);
        using (var scope = provider.CreateScope()) await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
        using (var scope = provider.CreateScope())
            await FluentActions.Awaiting(() => scope.ServiceProvider.GetRequiredService<ISender>().Send(command with { CustomerSubject = "other-owner" })).Should().ThrowAsync<NotificationConflictException>();
        using var read = provider.CreateScope(); var db = read.ServiceProvider.GetRequiredService<NotificationDbContext>();
        (await db.Messages.CountAsync(x => x.SourceEventId == command.EventId)).Should().Be(2); resolver.Calls.Should().Be(1);
    }
    [Fact]
    public async Task Consumer_faults_conflicting_source_and_unknown_status_without_recording_invalid_messages()
    {
        var resolver = new FixedRecipientResolver(); await using var provider = ConsumerTestHost.Create(infrastructure, resolver); await provider.InitializeNotificationDatabaseAsync();
        var conflictFaults = new ConcurrentBag<Fault<ShipmentCreated>>();
        var statusFaults = new ConcurrentBag<Fault<TrackingStatusChanged>>();
        var observer = Bus.Factory.CreateUsingRabbitMq(rabbit =>
        {
            rabbit.Host(infrastructure.RabbitMq.Hostname, infrastructure.RabbitMq.GetMappedPublicPort(5672), "/", host => { host.Username("rabbitmq"); host.Password("rabbitmq"); });
            rabbit.ReceiveEndpoint("notification-test-faults-" + Guid.NewGuid().ToString("N"), endpoint =>
            {
                endpoint.AutoDelete = true;
                endpoint.Handler<Fault<ShipmentCreated>>(context => { conflictFaults.Add(context.Message); return Task.CompletedTask; });
                endpoint.Handler<Fault<TrackingStatusChanged>>(context => { statusFaults.Add(context.Message); return Task.CompletedTask; });
            });
        });
        var bus = provider.GetRequiredService<IBusControl>(); await observer.StartAsync(); await bus.StartAsync();
        try
        {
            var original = new RecordNotification(Guid.NewGuid(), Guid.NewGuid(), Domain.NotificationKind.ShipmentCreated, DateTimeOffset.UtcNow,
                FixedRecipientResolver.Owner, FixedRecipientResolver.Tracking, null, null, null, null, null);
            using (var scope = provider.CreateScope()) await scope.ServiceProvider.GetRequiredService<ISender>().Send(original);
            await bus.Publish(new ShipmentCreated(original.EventId, original.ShipmentId, original.OccurredAtUtc, original.ShipmentId,
                FixedRecipientResolver.Tracking, "contradictory-owner", 10m, "EUR"));
            var invalidStatusId = Guid.NewGuid();
            await bus.Publish(new TrackingStatusChanged(invalidStatusId, original.ShipmentId, original.OccurredAtUtc, original.ShipmentId,
                FixedRecipientResolver.Tracking, (TrackingStatus)999, null, Guid.NewGuid()));
            await ConsumerTestHost.Until(() => Task.FromResult(conflictFaults.Any(x => x.Message.EventId == original.EventId) && statusFaults.Any(x => x.Message.EventId == invalidStatusId)));
            using var read = provider.CreateScope(); var db = read.ServiceProvider.GetRequiredService<NotificationDbContext>();
            (await db.Messages.CountAsync(x => x.SourceEventId == original.EventId)).Should().Be(2);
            (await db.Events.AnyAsync(x => x.EventId == invalidStatusId)).Should().BeFalse(); resolver.Calls.Should().Be(1);
            conflictFaults.Single(x => x.Message.EventId == original.EventId).Exceptions.Should().Contain(x => x.ExceptionType.Contains(nameof(NotificationConflictException)));
        }
        finally { await bus.StopAsync(); await observer.StopAsync(); }
    }
}
