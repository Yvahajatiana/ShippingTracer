using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.Tracking.Domain;

namespace ParcelFlow.Tracking.Infrastructure.Persistence;

public static class DatabaseInitialization
{
    public static async Task InitializeTrackingDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
        if (!bool.TryParse(scope.ServiceProvider.GetRequiredService<IConfiguration>()["Database:SeedDemoData"], out var seed) || !seed)
            return;
        foreach (var (id, number, status, eventId) in new[]
        {
            (Guid.Parse("40000000-0000-0000-0000-000000000001"), "PF400000000000000000000001", ParcelStatus.InTransit, Guid.Parse("41000000-0000-0000-0000-000000000001")),
            (Guid.Parse("40000000-0000-0000-0000-000000000002"), "PF400000000000000000000002", ParcelStatus.Delivered, Guid.Parse("41000000-0000-0000-0000-000000000002"))
        })
        {
            if (await database.Records.AnyAsync(value => value.ShipmentId == id, cancellationToken)) continue;
            var record = new TrackingRecord(id);
            record.Register(number);
            record.AddEntry(new TrackingEntry(id, ParcelStatus.Created, new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero), null));
            record.AddEntry(new TrackingEntry(eventId, status, new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero), "FR-LYON-01"));
            database.Records.Add(record);
        }
        await database.SaveChangesAsync(cancellationToken);
    }
}
