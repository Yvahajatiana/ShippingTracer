using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.Delivery.Domain;

namespace ParcelFlow.Delivery.Infrastructure.Persistence;

public static class DatabaseInitialization
{
    public static async Task InitializeDeliveryDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
        if (!bool.TryParse(scope.ServiceProvider.GetRequiredService<IConfiguration>()["Database:SeedDemoData"], out var seed) || !seed) return;
        foreach (var (subject, name) in new[] { ("demo-driver-lyon", "Camille Martin"), ("demo-driver-paris", "Alex Durand") })
            if (!await database.Drivers.AnyAsync(x => x.Subject == subject, cancellationToken)) database.Drivers.Add(new DeliveryDriver(subject, name));
        var routeId = Guid.Parse("60000000-0000-0000-0000-000000000001");
        if (!await database.Routes.AnyAsync(x => x.Id == routeId, cancellationToken))
            database.Routes.Add(new DeliveryRoute(routeId, "Lyon Gerland — matin", new DateOnly(2026, 10, 18)));
        foreach (var (id, number) in new[]
        {
            (Guid.Parse("61000000-0000-0000-0000-000000000001"), "PF600000000000000000000001"),
            (Guid.Parse("61000000-0000-0000-0000-000000000002"), "PF600000000000000000000002")
        })
        {
            if (await database.Parcels.AnyAsync(x => x.ShipmentId == id, cancellationToken)) continue;
            var parcel = new DeliveryParcel(id); parcel.Register(number); parcel.MarkPickedUp(); database.Parcels.Add(parcel);
        }
        await database.SaveChangesAsync(cancellationToken);
    }
}
