using MassTransit;
using Microsoft.EntityFrameworkCore;
using ParcelFlow.Notification.Domain;

namespace ParcelFlow.Notification.Infrastructure.Persistence;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<NotificationEvent> Events => Set<NotificationEvent>();
    public DbSet<NotificationMessage> Messages => Set<NotificationMessage>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<NotificationEvent>(entity =>
        {
            entity.ToTable("notification_events"); entity.HasKey(x => x.EventId);
            entity.Property(x => x.EventId).ValueGeneratedNever();
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.CustomerSubject).HasMaxLength(200);
            entity.Property(x => x.TrackingNumber).HasMaxLength(26);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.SourceEventId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(x => x.Messages).HasField("_messages").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        builder.Entity<NotificationMessage>(entity =>
        {
            entity.ToTable("notification_messages"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.CustomerSubject).HasMaxLength(200);
            entity.Property(x => x.TrackingNumber).HasMaxLength(26);
            entity.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
            entity.Property(x => x.Destination).HasMaxLength(254);
            entity.Property(x => x.Subject).HasMaxLength(200);
            entity.Property(x => x.Body).HasMaxLength(2000);
            entity.Ignore(x => x.DeliveryStatus);
            entity.HasIndex(x => new { x.SourceEventId, x.Channel }).IsUnique();
            entity.HasIndex(x => new { x.CustomerSubject, x.RecordedAtUtc, x.Id });
            entity.HasIndex(x => new { x.ShipmentId, x.RecordedAtUtc, x.Id });
        });
        builder.AddInboxStateEntity(); builder.AddOutboxMessageEntity(); builder.AddOutboxStateEntity();
    }
}
