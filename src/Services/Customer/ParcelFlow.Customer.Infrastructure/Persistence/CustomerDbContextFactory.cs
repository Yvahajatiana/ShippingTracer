using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ParcelFlow.Customer.Infrastructure.Persistence;

public sealed class CustomerDbContextFactory : IDesignTimeDbContextFactory<CustomerDbContext>
{
    public CustomerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CustomerDatabase")
            ?? "Host=localhost;Port=5432;Database=parcelflow_customers;Username=parcelflow";
        return new CustomerDbContext(new DbContextOptionsBuilder<CustomerDbContext>()
            .UseNpgsql(connectionString).Options);
    }
}
