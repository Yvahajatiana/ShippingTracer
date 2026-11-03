using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using MassTransit.EntityFrameworkCoreIntegration;
using MediatR;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParcelFlow.BuildingBlocks.Application.Correlation;
using ParcelFlow.Notification.Application;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.Notification.Domain;
using ParcelFlow.Notification.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Notification.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceTests(InfrastructureFixture infrastructure)
{
    private NotificationDbContext CreateContext() => new(new DbContextOptionsBuilder<NotificationDbContext>().UseNpgsql(infrastructure.Postgres.GetConnectionString()).Options);
    internal static NotificationEvent Event(Guid? id = null)
    {
        var item = new NotificationEvent(id ?? Guid.NewGuid(), Guid.NewGuid(), NotificationKind.ShipmentCreated,
            DateTimeOffset.UtcNow.AddTicks(7), "notification-owner", "PF800000000000000000000001", null, null, null, null, null);
        item.RecordMessages("notification-owner", item.TrackingNumber!, "owner@example.test", "+33600000001", "Envoi créé", "Votre envoi est créé.", "Envoi créé.", DateTimeOffset.UtcNow);
        return item;
    }
    [Fact]
    public async Task Migration_roundtrip_preserves_immutable_sources_messages_and_microseconds()
    {
        await using var database = CreateContext(); await database.Database.MigrateAsync();
        var item = Event(); database.Events.Add(item); await database.SaveChangesAsync(); database.ChangeTracker.Clear();
        var restored = await database.Events.Include(x => x.Messages).SingleAsync(x => x.EventId == item.EventId);
        restored.OccurredAtUtc.Should().Be(item.OccurredAtUtc); restored.Messages.Should().HaveCount(2);
        restored.Messages.Select(x => x.Channel).Should().BeEquivalentTo(new[] { NotificationChannel.Email, NotificationChannel.Sms });
        restored.Messages.Should().OnlyContain(x => x.DeliveryStatus == "MockSent" && x.SourceEventId == item.EventId);
        (await database.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }
    [Fact]
    public async Task Required_source_relationship_prevents_deleting_notified_source()
    {
        await using var database = CreateContext(); await database.Database.MigrateAsync(); var item = Event(); database.Events.Add(item); await database.SaveChangesAsync();
        var failure = await FluentActions.Awaiting(() => database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM notification_events WHERE \"EventId\" = {item.EventId}")).Should().ThrowAsync<PostgresException>();
        failure.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }
    [Fact]
    public async Task Database_unique_index_rejects_second_message_for_same_source_channel()
    {
        await using var database = CreateContext(); await database.Database.MigrateAsync(); var item = Event(); database.Events.Add(item); await database.SaveChangesAsync();
        var message = item.Messages.First(); var id = Guid.NewGuid();
        var failure = await FluentActions.Awaiting(() => database.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO notification_messages (\"Id\", \"SourceEventId\", \"ShipmentId\", \"CustomerSubject\", \"TrackingNumber\", \"Channel\", \"Destination\", \"Subject\", \"Body\", \"OccurredAtUtc\", \"RecordedAtUtc\") SELECT {id}, \"SourceEventId\", \"ShipmentId\", \"CustomerSubject\", \"TrackingNumber\", \"Channel\", \"Destination\", \"Subject\", \"Body\", \"OccurredAtUtc\", \"RecordedAtUtc\" FROM notification_messages WHERE \"Id\" = {message.Id}")).Should().ThrowAsync<PostgresException>();
        failure.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }
    [Fact]
    public async Task Failure_after_saving_command_rolls_back_source_messages_and_real_bus_outbox()
    {
        await using var services = ConsumerTestHost.Create(infrastructure, new FixedRecipientResolver(), registrations =>
        {
            registrations.AddScoped<ParcelFlow.Notification.Infrastructure.Persistence.NotificationStore>();
            registrations.RemoveAll<INotificationStore>();
            registrations.AddScoped<INotificationStore>(provider => new FailingSaveStore(provider.GetRequiredService<ParcelFlow.Notification.Infrastructure.Persistence.NotificationStore>()));
        });
        await services.InitializeNotificationDatabaseAsync();
        var command = new RecordNotification(Guid.NewGuid(), Guid.NewGuid(), NotificationKind.ShipmentCreated, DateTimeOffset.UtcNow,
            FixedRecipientResolver.Owner, FixedRecipientResolver.Tracking, null, null, null, null, null);
        using (var scope = services.CreateScope())
        {
            // Ambient correlation as set by the consume filter, so the outbox rows of this flow can be found.
            scope.ServiceProvider.GetRequiredService<CorrelationContext>().Set(command.ShipmentId);
            await FluentActions.Awaiting(() => scope.ServiceProvider.GetRequiredService<ISender>().Send(command)).Should().ThrowAsync<InvalidOperationException>().WithMessage("Injected save failure.");
        }
        await using var read = CreateContext();
        (await read.Events.AnyAsync(x => x.EventId == command.EventId)).Should().BeFalse();
        (await read.Messages.AnyAsync(x => x.SourceEventId == command.EventId)).Should().BeFalse();
        (await read.Set<OutboxMessage>().AnyAsync(x => x.CorrelationId == command.ShipmentId)).Should().BeFalse();
    }
    [Fact]
    public async Task Seed_is_opt_in_idempotent_and_does_not_publish_messages()
    {
        await using var services = ConsumerTestHost.Create(infrastructure, new FixedRecipientResolver());
        await services.InitializeNotificationDatabaseAsync(true); await services.InitializeNotificationDatabaseAsync(true);
        await using var scope = services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var ids = new[] { Guid.Parse("80000000-0000-0000-0000-000000000001"), Guid.Parse("80000000-0000-0000-0000-000000000002") };
        (await db.Events.CountAsync(x => ids.Contains(x.EventId))).Should().Be(2);
        (await db.Messages.CountAsync(x => ids.Contains(x.SourceEventId))).Should().Be(4);
        (await db.Set<OutboxMessage>().AnyAsync(x => x.Body.Contains("80000000-0000-0000-0000-000000000001") || x.Body.Contains("80000000-0000-0000-0000-000000000002"))).Should().BeFalse();
    }
    private sealed class FailingSaveStore(INotificationStore inner) : INotificationStore
    {
        public Task LockEventAsync(Guid id, CancellationToken ct) => inner.LockEventAsync(id, ct);
        public Task<NotificationEvent?> FindEventAsync(Guid id, CancellationToken ct) => inner.FindEventAsync(id, ct);
        public void AddEvent(NotificationEvent item) => inner.AddEvent(item);
        public async Task SaveChangesAsync(CancellationToken ct) { await inner.SaveChangesAsync(ct); throw new InvalidOperationException("Injected save failure."); }
        public Task<NotificationMessage?> FindNotificationAsync(Guid id, CancellationToken ct) => inner.FindNotificationAsync(id, ct);
        public Task<NotificationPageDto> ListNotificationsAsync(string? owner, Guid? shipment, int page, int size, CancellationToken ct) => inner.ListNotificationsAsync(owner, shipment, page, size, ct);
    }
}
