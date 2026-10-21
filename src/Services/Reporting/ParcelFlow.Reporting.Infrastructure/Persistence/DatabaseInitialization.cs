using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.Reporting.Domain;
namespace ParcelFlow.Reporting.Infrastructure.Persistence;
public static class DatabaseInitialization
{
    public static async Task InitializeReportingDatabaseAsync(this IServiceProvider services, bool seedDemoData = false, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
        if (!seedDemoData) return;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(18062608)", cancellationToken);
        for (var index = 1; index <= 3; index++)
        {
            var shipmentId = Guid.Parse($"90000000-0000-0000-0000-{index:D12}");
            if (await database.Shipments.AnyAsync(x => x.ShipmentId == shipmentId, cancellationToken)) continue;
            var shipment = new ShipmentMetrics(shipmentId, "PF9" + index.ToString("X23"));
            var created = new DateTimeOffset(2026, 10, index == 2 ? 16 : 15, index == 3 ? 9 : 8, 0, 0, TimeSpan.Zero);
            void Add(int sequence, ParcelStatus status, DateTimeOffset occurredAt)
            {
                var eventId = Guid.Parse($"91000000-0000-0000-0000-{index * 10 + sequence:D12}");
                var sourceId = Guid.Parse($"92000000-0000-0000-0000-{index * 10 + sequence:D12}");
                shipment.AddFact(new StatusFact(eventId, sourceId, shipmentId, status, occurredAt, null));
            }
            Add(1, ParcelStatus.Created, created); Add(2, ParcelStatus.PickedUp, created.AddHours(2));
            if (index == 2) Add(3, ParcelStatus.InTransit, new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero));
            else Add(3, ParcelStatus.Delivered, created.AddHours(index == 1 ? 26 : 24));
            if (index == 3) Add(4, ParcelStatus.Returned, created.AddDays(2));
            database.Shipments.Add(shipment);
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
