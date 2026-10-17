using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.Shipment.Domain;

namespace ParcelFlow.Shipment.Infrastructure.Persistence;

public static class DatabaseInitialization
{
    public static async Task InitializeShipmentDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ShipmentDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (!bool.TryParse(configuration["Database:SeedDemoData"], out var seed) || !seed)
            return;

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        foreach (var tariff in new[]
        {
            new Tariff(DeliveryZone.Domestic, 4.90m, 1.20m),
            new Tariff(DeliveryZone.Europe, 9.90m, 2.50m),
            new Tariff(DeliveryZone.International, 19.90m, 5m)
        })
        {
            if (!await database.Tariffs.AnyAsync(value => value.Zone == tariff.Zone, cancellationToken))
                database.Tariffs.Add(tariff);
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
