using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ParcelFlow.Billing.Infrastructure.Persistence;

public sealed class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<BillingDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__BillingDatabase")
            ?? "Host=localhost;Port=5432;Database=parcelflow_billing;Username=parcelflow").Options);
}
