using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParcelFlow.BuildingBlocks.Application.Identity;
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.Shipment.Domain;
using ParcelFlow.Shipment.Infrastructure;
using ParcelFlow.Shipment.Infrastructure.Persistence;
using Xunit;
using System.Collections.Concurrent;

namespace ParcelFlow.Shipment.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceSagaTests(InfrastructureFixture infrastructure)
{
    private ShipmentDbContext CreateContext() => new(new DbContextOptionsBuilder<ShipmentDbContext>()
        .UseNpgsql(infrastructure.Postgres.GetConnectionString()).Options);

    private IHost CreateHost(TimeSpan? paymentTimeout = null, CapturingLoggerProvider? logs = null) => Host.CreateDefaultBuilder()
        .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ShipmentDatabase"] = infrastructure.Postgres.GetConnectionString(),
            ["ConnectionStrings:Redis"] = infrastructure.Redis.GetConnectionString(),
            ["RabbitMq:Host"] = infrastructure.RabbitMq.Hostname,
            ["RabbitMq:Port"] = infrastructure.RabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:Username"] = "rabbitmq",
            ["RabbitMq:Password"] = "rabbitmq",
            ["Database:SeedDemoData"] = "true",
            ["ShipmentBooking:PaymentTimeout"] = paymentTimeout?.ToString("c", System.Globalization.CultureInfo.InvariantCulture)
        }))
        .ConfigureLogging(logging =>
        {
            if (logs is not null) logging.AddProvider(logs);
        })
        .ConfigureServices((context, services) => services.AddShipmentInfrastructure(context.Configuration)).Build();

    [Fact]
    public async Task Saga_commits_payment_pickup_and_retains_terminal_state_against_repeated_creation()
    {
        using var host = CreateHost();
        await host.Services.InitializeShipmentDatabaseAsync();
        await host.StartAsync();
        try
        {
            var shipment = PersistenceTests.CreateShipment();
            await SeedShipment(shipment);
            var bus = host.Services.GetRequiredService<IBus>();
            var paymentRequests = new ConcurrentBag<Guid>();
            var readyEvents = new ConcurrentBag<Guid>();
            var observer = bus.ConnectReceiveEndpoint($"shipment-test-{Guid.NewGuid():N}", endpoint =>
            {
                endpoint.Handler<ShipmentReadyForPickup>(context =>
                {
                    if (context.Message.ShipmentId == shipment.Id) readyEvents.Add(context.Message.EventId);
                    return Task.CompletedTask;
                });
            });
            await observer.Ready;
            var created = new ShipmentCreated(Guid.NewGuid(), shipment.Id, DateTimeOffset.UtcNow, shipment.Id,
                shipment.TrackingNumber, shipment.OwnerSubject, shipment.Price, shipment.Currency);
            await bus.Publish(created, context => context.MessageId = created.EventId);
            await WaitFor(shipment.Id, "AwaitingPayment", ShipmentStatus.AwaitingPayment);
            await WaitForInbox(created.EventId, delivered: true);
            // Billing starts after creation: its durable queue must retain the payment command.
            var payments = bus.ConnectReceiveEndpoint("billing-payments", endpoint =>
                endpoint.Handler<PaymentRequested>(context =>
                {
                    if (context.Message.ShipmentId == shipment.Id) paymentRequests.Add(context.Message.EventId);
                    return Task.CompletedTask;
                }));
            await payments.Ready;
            // A mismatched payment must never authorize physical pickup.
            await bus.Publish(new PaymentCompleted(Guid.NewGuid(), shipment.Id, DateTimeOffset.UtcNow, shipment.Id, 1m, "USD"));
            var paid = new PaymentCompleted(Guid.NewGuid(), shipment.Id, DateTimeOffset.UtcNow, shipment.Id, shipment.Price, "EUR");
            await bus.Publish(paid, context => context.MessageId = paid.EventId);
            await WaitFor(shipment.Id, "AwaitingPickup", ShipmentStatus.ReadyForPickup);
            await bus.Publish(paid, context => context.MessageId = paid.EventId);
            var pickup = new ParcelPickedUp(Guid.NewGuid(), shipment.Id, DateTimeOffset.UtcNow, shipment.Id);
            await bus.Publish(pickup, context => context.MessageId = pickup.EventId);
            await WaitFor(shipment.Id, "Completed", ShipmentStatus.PickedUp);
            var repeated = created with { EventId = Guid.NewGuid() };
            await bus.Publish(repeated, context => context.MessageId = repeated.EventId);
            // Publish to the same saga queue, then wait for its inbox acknowledgement.
            await WaitForInbox(repeated.EventId);
            await WaitFor(shipment.Id, "Completed", ShipmentStatus.PickedUp);
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                while (paymentRequests.IsEmpty || readyEvents.IsEmpty)
                    await Task.Delay(100, deadline.Token);
            paymentRequests.Should().ContainSingle();
            readyEvents.Should().ContainSingle();
            await observer.StopAsync();
            await payments.StopAsync();
        }
        finally { await host.StopAsync(); }
    }

    [Fact]
    public async Task Payment_failure_is_terminal_and_demo_tariff_seed_is_idempotent()
    {
        using var host = CreateHost();
        await host.Services.InitializeShipmentDatabaseAsync();
        await host.Services.InitializeShipmentDatabaseAsync();
        await using (var database = CreateContext())
            (await database.Tariffs.CountAsync()).Should().Be(3);
        await host.StartAsync();
        try
        {
            var shipment = PersistenceTests.CreateShipment();
            await SeedShipment(shipment);
            var bus = host.Services.GetRequiredService<IBus>();
            await bus.Publish(new ShipmentCreated(Guid.NewGuid(), shipment.Id, DateTimeOffset.UtcNow, shipment.Id,
                shipment.TrackingNumber, shipment.OwnerSubject, shipment.Price, shipment.Currency));
            await WaitFor(shipment.Id, "AwaitingPayment", ShipmentStatus.AwaitingPayment);
            await bus.Publish(new PaymentFailed(Guid.NewGuid(), shipment.Id, DateTimeOffset.UtcNow, shipment.Id, "Mock declined"));
            await WaitFor(shipment.Id, "PaymentRejected", ShipmentStatus.PaymentFailed);
            var late = new PaymentCompleted(Guid.NewGuid(), shipment.Id, DateTimeOffset.UtcNow, shipment.Id, shipment.Price, "EUR");
            await bus.Publish(late, context => context.MessageId = late.EventId);
            await WaitForInbox(late.EventId);
            await WaitFor(shipment.Id, "PaymentRejected", ShipmentStatus.PaymentFailed);
        }
        finally { await host.StopAsync(); }
    }

    [Fact]
    public async Task Mismatched_payment_is_logged_and_a_valid_payment_cancels_the_timeout()
    {
        var logs = new CapturingLoggerProvider();
        using var host = CreateHost(TimeSpan.FromSeconds(3), logs);
        await host.Services.InitializeShipmentDatabaseAsync();
        await host.StartAsync();
        try
        {
            var shipment = PersistenceTests.CreateShipment();
            await SeedShipment(shipment);
            var bus = host.Services.GetRequiredService<IBus>();
            var timedOut = new ConcurrentBag<Guid>();
            var observer = bus.ConnectReceiveEndpoint($"shipment-test-{Guid.NewGuid():N}", endpoint =>
                endpoint.Handler<ShipmentPaymentTimedOut>(context =>
                {
                    if (context.Message.ShipmentId == shipment.Id) timedOut.Add(context.Message.EventId);
                    return Task.CompletedTask;
                }));
            await observer.Ready;
            await bus.Publish(new ShipmentCreated(Guid.NewGuid(), shipment.Id, DateTimeOffset.UtcNow, shipment.Id,
                shipment.TrackingNumber, shipment.OwnerSubject, shipment.Price, shipment.Currency));
            await WaitFor(shipment.Id, "AwaitingPayment", ShipmentStatus.AwaitingPayment);
            var mismatched = new PaymentCompleted(Guid.NewGuid(), shipment.Id, DateTimeOffset.UtcNow, shipment.Id, shipment.Price, "USD");
            await bus.Publish(mismatched, context => context.MessageId = mismatched.EventId);
            await WaitForInbox(mismatched.EventId);
            await WaitFor(shipment.Id, "AwaitingPayment", ShipmentStatus.AwaitingPayment);
            var warning = logs.Entries.Should().ContainSingle(entry => entry.EventId == 3101).Subject;
            warning.Level.Should().Be(LogLevel.Warning);
            warning.Message.Should().Contain(shipment.Id.ToString()).And.Contain(mismatched.EventId.ToString())
                .And.Contain("EUR").And.Contain("USD").And.NotContain(shipment.OwnerSubject);
            var paid = new PaymentCompleted(Guid.NewGuid(), shipment.Id, DateTimeOffset.UtcNow, shipment.Id, shipment.Price, "EUR");
            await bus.Publish(paid, context => context.MessageId = paid.EventId);
            await WaitFor(shipment.Id, "AwaitingPickup", ShipmentStatus.ReadyForPickup);
            await using (var database = CreateContext())
                (await database.Bookings.AsNoTracking().SingleAsync(value => value.CorrelationId == shipment.Id))
                    .PaymentTimeoutTokenId.Should().BeNull();
            // The broker still delivers the delayed timeout, but its cleared token makes it a no-op.
            await Task.Delay(TimeSpan.FromSeconds(6));
            await WaitFor(shipment.Id, "AwaitingPickup", ShipmentStatus.ReadyForPickup);
            timedOut.Should().BeEmpty();
            logs.Entries.Should().NotContain(entry => entry.EventId == 3102);
            await observer.StopAsync();
        }
        finally { await host.StopAsync(); }
    }

    [Fact]
    public async Task Unconfirmed_payment_times_out_into_a_terminal_state_and_ignores_late_payment()
    {
        var logs = new CapturingLoggerProvider();
        using var host = CreateHost(TimeSpan.FromSeconds(2), logs);
        await host.Services.InitializeShipmentDatabaseAsync();
        await host.StartAsync();
        try
        {
            var shipment = PersistenceTests.CreateShipment();
            await SeedShipment(shipment);
            var bus = host.Services.GetRequiredService<IBus>();
            var timedOut = new ConcurrentBag<ConsumeContext<ShipmentPaymentTimedOut>>();
            var observer = bus.ConnectReceiveEndpoint($"shipment-test-{Guid.NewGuid():N}", endpoint =>
                endpoint.Handler<ShipmentPaymentTimedOut>(context =>
                {
                    if (context.Message.ShipmentId == shipment.Id) timedOut.Add(context);
                    return Task.CompletedTask;
                }));
            await observer.Ready;
            var created = new ShipmentCreated(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, shipment.Id,
                shipment.TrackingNumber, shipment.OwnerSubject, shipment.Price, shipment.Currency);
            await bus.Publish(created, context => context.MessageId = created.EventId);
            await WaitFor(shipment.Id, "AwaitingPayment", ShipmentStatus.AwaitingPayment);
            // A mismatched payment alone must not keep the booking open past its deadline.
            await bus.Publish(new PaymentCompleted(Guid.NewGuid(), shipment.Id, DateTimeOffset.UtcNow, shipment.Id, 1m, "EUR"));
            await WaitFor(shipment.Id, "PaymentTimedOut", ShipmentStatus.PaymentFailed);
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                while (timedOut.IsEmpty)
                    await Task.Delay(100, deadline.Token);
            var timeoutContext = timedOut.Should().ContainSingle().Subject;
            var timeoutEvent = timeoutContext.Message;
            // Saga events carry a deterministic EventId, used as transport MessageId, and the flow correlation.
            timeoutEvent.EventId.Should().Be(DeterministicGuid.Create("shipment:payment-timed-out:", shipment.Id));
            timeoutContext.MessageId.Should().Be(timeoutEvent.EventId);
            timeoutContext.CorrelationId.Should().Be(created.CorrelationId);
            timeoutEvent.CorrelationId.Should().Be(created.CorrelationId);
            timeoutEvent.TrackingNumber.Should().Be(shipment.TrackingNumber);
            logs.Entries.Should().Contain(entry => entry.EventId == 3101);
            logs.Entries.Should().ContainSingle(entry => entry.EventId == 3102 && entry.Level == LogLevel.Warning);
            var late = new PaymentCompleted(Guid.NewGuid(), shipment.Id, DateTimeOffset.UtcNow, shipment.Id, shipment.Price, "EUR");
            await bus.Publish(late, context => context.MessageId = late.EventId);
            await WaitForInbox(late.EventId);
            await WaitFor(shipment.Id, "PaymentTimedOut", ShipmentStatus.PaymentFailed);
            logs.Entries.Should().ContainSingle(entry => entry.EventId == 3103 && entry.Message.Contains(late.EventId.ToString()));
            await observer.StopAsync();
        }
        finally { await host.StopAsync(); }
    }

    private async Task SeedShipment(ParcelShipment shipment)
    {
        await using var database = CreateContext();
        database.Shipments.Add(shipment);
        await database.SaveChangesAsync();
    }

    private async Task WaitFor(Guid id, string state, ShipmentStatus status)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            await using var database = CreateContext();
            var saga = await database.Bookings.AsNoTracking().SingleOrDefaultAsync(value => value.CorrelationId == id, deadline.Token);
            var shipment = await database.Shipments.AsNoTracking().SingleAsync(value => value.Id == id, deadline.Token);
            if (saga?.CurrentState == state && shipment.Status == status) return;
            await Task.Delay(100, deadline.Token);
        }
    }

    private async Task WaitForInbox(Guid messageId, bool delivered = false)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            await using var database = CreateContext();
            if (await database.Set<MassTransit.EntityFrameworkCoreIntegration.InboxState>()
                .AnyAsync(value => value.MessageId == messageId && value.Consumed != null &&
                    (!delivered || value.Delivered != null), deadline.Token)) return;
            await Task.Delay(100, deadline.Token);
        }
    }
}

public sealed record CapturedLog(string Category, LogLevel Level, int EventId, string Message);

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> entries = new();

    public IReadOnlyCollection<CapturedLog> Entries => entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, entries);

    public void Dispose() { }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel) && category.StartsWith("ParcelFlow.Shipment", StringComparison.Ordinal))
                entries.Enqueue(new CapturedLog(category, logLevel, eventId.Id, formatter(state, exception)));
        }
    }
}
