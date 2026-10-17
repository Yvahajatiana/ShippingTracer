using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ParcelFlow.Tracking.Infrastructure.Persistence;

public sealed class TrackingDbContextFactory : IDesignTimeDbContextFactory<TrackingDbContext>
{
    public TrackingDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<TrackingDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__TrackingDatabase")
            ?? "Host=localhost;Port=5432;Database=parcelflow_tracking;Username=parcelflow").Options);
}
