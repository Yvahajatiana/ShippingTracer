using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.Notification.Domain;

namespace ParcelFlow.Notification.Infrastructure.Persistence;

public static class DatabaseInitialization
{
    public static async Task InitializeNotificationDatabaseAsync(this IServiceProvider services, bool seedDemoData = false, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
        if (!seedDemoData) return;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(18062607)", cancellationToken);
        for (var index = 1; index <= 2; index++)
        {
            var eventId = Guid.Parse($"80000000-0000-0000-0000-{index:D12}");
            if (await database.Events.AnyAsync(x => x.EventId == eventId, cancellationToken)) continue;
            var shipmentId = Guid.Parse($"81000000-0000-0000-0000-{index:D12}");
            var owner = $"10000000-0000-0000-0000-{index:D12}";
            var tracking = "PF8" + index.ToString("X23");
            var time = new DateTimeOffset(2026, 10, 18, 8 + index, 0, 0, TimeSpan.Zero);
            var item = new NotificationEvent(eventId, shipmentId, NotificationKind.ShipmentCreated, time, owner, tracking, null, null, null, null, null);
            item.RecordMessages(owner, tracking, $"demo{index}@example.test", $"+3360000000{index}", "Envoi créé",
                $"Votre colis {tracking} a été créé.", $"Colis {tracking} créé.", time);
            database.Events.Add(item);
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
