using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ParcelFlow.Delivery.Infrastructure.Persistence;

public sealed class DeliveryDbContextFactory : IDesignTimeDbContextFactory<DeliveryDbContext>
{
    public DeliveryDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<DeliveryDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__DeliveryDatabase")
            ?? "Host=localhost;Port=5432;Database=parcelflow_delivery;Username=parcelflow").Options);
}
