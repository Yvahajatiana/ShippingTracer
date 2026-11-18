using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ParcelFlow.Delivery.Infrastructure.Persistence;

public sealed class DeliveryDbContextFactory : IDesignTimeDbContextFactory<DeliveryDbContext>
{
    // Migrations never read nor write proofs: a throwaway key only satisfies the model.
    public DeliveryDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<DeliveryDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__DeliveryDatabase")
            ?? "Host=localhost;Port=5432;Database=parcelflow_delivery;Username=parcelflow").Options,
        new DeliveryProofProtector(new DeliveryProofSettings(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), [], DeliveryProofSettings.DefaultRetentionDays, TimeSpan.FromHours(1))));
}
