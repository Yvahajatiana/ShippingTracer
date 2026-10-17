using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ParcelFlow.Warehouse.Infrastructure.Persistence;

public sealed class WarehouseDbContextFactory : IDesignTimeDbContextFactory<WarehouseDbContext>
{
    public WarehouseDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<WarehouseDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__WarehouseDatabase")
            ?? "Host=localhost;Port=5432;Database=parcelflow_warehouse;Username=parcelflow").Options);
}
