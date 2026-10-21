using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ParcelFlow.Reporting.Infrastructure.Persistence;

public sealed class ReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ReportingDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__ReportingDatabase")
            ?? "Host=localhost;Port=5432;Database=parcelflow_reporting;Username=parcelflow").Options);
}

