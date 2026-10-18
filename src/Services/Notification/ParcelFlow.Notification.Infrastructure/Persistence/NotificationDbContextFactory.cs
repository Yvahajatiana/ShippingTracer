using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ParcelFlow.Notification.Infrastructure.Persistence;

public sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<NotificationDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__NotificationDatabase")
            ?? "Host=localhost;Port=5432;Database=parcelflow_notification;Username=parcelflow").Options);
}
