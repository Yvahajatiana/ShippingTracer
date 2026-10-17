using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.Warehouse.Domain;

namespace ParcelFlow.Warehouse.Infrastructure.Persistence;

public static class DatabaseInitialization
{
    public static async Task InitializeWarehouseDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
        if (!bool.TryParse(scope.ServiceProvider.GetRequiredService<IConfiguration>()["Database:SeedDemoData"], out var seed) || !seed) return;
        var lyon = Guid.Parse("50000000-0000-0000-0000-000000000001");
        var paris = Guid.Parse("50000000-0000-0000-0000-000000000002");
        if (!await database.Warehouses.AnyAsync(x => x.Id == lyon, cancellationToken))
            database.Warehouses.Add(new WarehouseSite(lyon, "FR-LYON-01", "Entrepôt Lyon Gerland", WarehouseKind.Warehouse,
                "18 avenue Tony Garnier", "Lyon", "69007", "FR"));
        if (!await database.Warehouses.AnyAsync(x => x.Id == paris, cancellationToken))
            database.Warehouses.Add(new WarehouseSite(paris, "FR-PARIS-01", "Centre de tri Paris Nord", WarehouseKind.SortingCenter,
                "24 rue de la Haie Coq", "Aubervilliers", "93300", "FR"));
        foreach (var (id, number) in new[]
        {
            (Guid.Parse("51000000-0000-0000-0000-000000000001"), "PF500000000000000000000001"),
            (Guid.Parse("51000000-0000-0000-0000-000000000002"), "PF500000000000000000000002")
        })
        {
            if (!await database.Inventory.AnyAsync(x => x.ShipmentId == id, cancellationToken))
                database.Inventory.Add(new ParcelInventory(id, number));
        }
        await database.SaveChangesAsync(cancellationToken);
    }
}
