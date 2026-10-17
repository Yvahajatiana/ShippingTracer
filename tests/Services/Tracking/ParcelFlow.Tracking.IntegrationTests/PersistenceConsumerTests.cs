using System.Collections.Concurrent;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Tracking.Application;
using ParcelFlow.Tracking.Infrastructure;
using ParcelFlow.Tracking.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Tracking.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceConsumerTests(InfrastructureFixture infrastructure)
{
    private TrackingDbContext CreateContext() => new(new DbContextOptionsBuilder<TrackingDbContext>()
        .UseNpgsql(infrastructure.Postgres.GetConnectionString()).Options);

    private IHost CreateHost() => Host.CreateDefaultBuilder().ConfigureAppConfiguration(configuration =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:TrackingDatabase"] = infrastructure.Postgres.GetConnectionString(),
            ["ConnectionStrings:Redis"] = infrastructure.Redis.GetConnectionString(),
            ["RabbitMq:Host"] = infrastructure.RabbitMq.Hostname,
            ["RabbitMq:Port"] = infrastructure.RabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:Username"] = "rabbitmq", ["RabbitMq:Password"] = "rabbitmq"
        })).ConfigureServices((context, services) =>
        services.AddTrackingApplication().AddTrackingInfrastructure(context.Configuration)).Build();

    [Fact]
    public async Task Demo_seed_is_explicit_and_idempotent()
    {
        using var host = CreateHost();
        var settings = host.Services.GetRequiredService<IConfiguration>();
        settings["Database:SeedDemoData"] = "true";
        await host.Services.InitializeTrackingDatabaseAsync();
        await host.Services.InitializeTrackingDatabaseAsync();
        await using var database = CreateContext();
        var demos = await database.Records.Include(x => x.Entries)
            .Where(x => x.TrackingNumber == "PF400000000000000000000001" || x.TrackingNumber == "PF400000000000000000000002")
            .ToListAsync();
        demos.Should().HaveCount(2);
        demos.Should().OnlyContain(x => x.Entries.Count == 2);
    }

    [Fact]
    public async Task Global_event_collision_rolls_back_tracking_record_and_outbox_publication()
    {
        using var host = CreateHost();
        await host.Services.InitializeTrackingDatabaseAsync();
        var original = PersistenceTests.CreateRecord();
        await using (var seed = CreateContext())
        {
            seed.Records.Add(original);
            await seed.SaveChangesAsync();
        }
        var failedShipment = Guid.NewGuid();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<MediatR.ISender>();
            var write = () => sender.Send(new RecordTrackingEvent(original.Entries.Single().EventId, failedShipment,
                "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant(), ParcelFlow.Tracking.Domain.ParcelStatus.Created,
                DateTimeOffset.UtcNow, null));
            await FluentActions.Awaiting(write).Should().ThrowAsync<DbUpdateException>();
        }
        await using var database = CreateContext();
        (await database.Records.AnyAsync(x => x.ShipmentId == failedShipment)).Should().BeFalse();
        (await database.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>()
            .AnyAsync(x => x.Body.Contains(failedShipment.ToString()))).Should().BeFalse();
    }

    [Fact]
    public async Task Malformed_logistics_fact_faults_without_creating_history()
    {
        using var host = CreateHost();
        await host.Services.InitializeTrackingDatabaseAsync();
        await host.StartAsync();
        var shipment = Guid.NewGuid();
        var fault = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bus = host.Services.GetRequiredService<IBus>();
        var handle = bus.ConnectReceiveEndpoint($"tracking-invalid-{Guid.NewGuid():N}", endpoint =>
            endpoint.Handler<Fault<ParcelStatusRecorded>>(context =>
            {
                if (context.Message.Message.ShipmentId == shipment) fault.TrySetResult(true);
                return Task.CompletedTask;
            }));
        await handle.Ready;
        try
        {
            await bus.Publish(new ParcelStatusRecorded(Guid.NewGuid(), shipment, DateTimeOffset.UtcNow,
                shipment, "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant(), TrackingStatus.InTransit, "invalid facility"));
            await fault.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await using var database = CreateContext();
            (await database.Records.AnyAsync(x => x.ShipmentId == shipment)).Should().BeFalse();
        }
        finally { await handle.StopAsync(); await host.StopAsync(); }
    }

    [Fact]
    public async Task Pickup_before_creation_and_new_transport_duplicate_produce_one_event_per_fact()
    {
        using var host = CreateHost();
        await host.Services.InitializeTrackingDatabaseAsync();
        await host.StartAsync();
        var shipment = Guid.NewGuid();
        var number = "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
        var observed = new ConcurrentBag<TrackingStatusChanged>();
        var bus = host.Services.GetRequiredService<IBus>();
        var handle = bus.ConnectReceiveEndpoint($"tracking-observer-{Guid.NewGuid():N}", endpoint =>
            endpoint.Handler<TrackingStatusChanged>(context =>
            {
                if (context.Message.ShipmentId == shipment) observed.Add(context.Message);
                return Task.CompletedTask;
            }));
        await handle.Ready;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var pickup = new ParcelPickedUp(Guid.NewGuid(), shipment,
                new DateTimeOffset(now.Ticks - now.Ticks % 10 + 7, TimeSpan.Zero), shipment);
            await bus.Publish(pickup);
            await WaitForEntries(shipment, 1);
            observed.Should().BeEmpty();
            var created = new ShipmentCreated(Guid.NewGuid(), shipment, pickup.OccurredAtUtc.AddMinutes(-1), shipment, number, "demo-customer", 7.30m, "EUR");
            await bus.Publish(created);
            await WaitForEntries(shipment, 2);
            await WaitUntil(() => observed.Count == 2);
            var duplicateMessageId = Guid.NewGuid();
            await bus.Publish(pickup, context => context.MessageId = duplicateMessageId);
            await WaitForInbox(duplicateMessageId);
            await using var database = CreateContext();
            var record = await database.Records.Include(x => x.Entries).SingleAsync(x => x.ShipmentId == shipment);
            record.TrackingNumber.Should().Be(number);
            record.Entries.Should().HaveCount(2);
            observed.Select(x => x.SourceEventId).Should().BeEquivalentTo(new[] { created.EventId, pickup.EventId });
        }
        finally { await handle.StopAsync(); await host.StopAsync(); }
    }

    [Fact]
    public async Task Concurrent_logistics_facts_for_one_shipment_all_survive_and_publish_exactly_once()
    {
        using var host = CreateHost();
        await host.Services.InitializeTrackingDatabaseAsync();
        await host.StartAsync();
        var shipment = Guid.NewGuid();
        var number = "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
        var observed = new ConcurrentBag<Guid>();
        var faults = new ConcurrentBag<Guid>();
        var bus = host.Services.GetRequiredService<IBus>();
        var handle = bus.ConnectReceiveEndpoint($"tracking-stress-{Guid.NewGuid():N}", endpoint =>
        {
            endpoint.Handler<TrackingStatusChanged>(context =>
            {
                if (context.Message.ShipmentId == shipment) observed.Add(context.Message.SourceEventId);
                return Task.CompletedTask;
            });
            endpoint.Handler<Fault<ParcelStatusRecorded>>(context =>
            {
                if (context.Message.Message.ShipmentId == shipment) faults.Add(context.Message.Message.EventId);
                return Task.CompletedTask;
            });
        });
        await handle.Ready;
        try
        {
            var facts = Enumerable.Range(0, 20).Select(i => new ParcelStatusRecorded(Guid.NewGuid(), shipment,
                DateTimeOffset.UtcNow.AddSeconds(i), shipment, number, TrackingStatus.InTransit, $"FR-HUB-{i:D2}")).ToArray();
            var pickup = new ParcelPickedUp(Guid.NewGuid(), shipment, DateTimeOffset.UtcNow.AddMinutes(-1), shipment);
            var created = new ShipmentCreated(Guid.NewGuid(), shipment, DateTimeOffset.UtcNow.AddMinutes(-2), shipment,
                number, "demo-customer", 7.30m, "EUR");
            await Task.WhenAll(facts.Select(fact => bus.Publish(fact)).Append(bus.Publish(pickup)).Append(bus.Publish(created)));
            await WaitForEntries(shipment, facts.Length + 2);
            await WaitUntil(() => observed.Count >= facts.Length + 2);
            observed.Should().OnlyHaveUniqueItems().And.HaveCount(facts.Length + 2);
            faults.Should().BeEmpty();
        }
        finally { await handle.StopAsync(); await host.StopAsync(); }
    }

    private async Task WaitForEntries(Guid shipment, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        while (true)
        {
            await using var database = CreateContext();
            if (await database.Entries.CountAsync(x => EF.Property<Guid>(x, "ShipmentId") == shipment, timeout.Token) == count) return;
            await Task.Delay(100, timeout.Token);
        }
    }

    private async Task WaitForInbox(Guid messageId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            await using var database = CreateContext();
            if (await database.Set<MassTransit.EntityFrameworkCoreIntegration.InboxState>()
                .AnyAsync(x => x.MessageId == messageId && x.Delivered != null, timeout.Token)) return;
            await Task.Delay(100, timeout.Token);
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!condition()) await Task.Delay(100, timeout.Token);
    }
}
