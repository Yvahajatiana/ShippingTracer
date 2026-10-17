using MassTransit;
using Microsoft.EntityFrameworkCore;
using ParcelFlow.Tracking.Domain;

namespace ParcelFlow.Tracking.Infrastructure.Persistence;

public sealed class TrackingDbContext(DbContextOptions<TrackingDbContext> options) : DbContext(options)
{
    public DbSet<TrackingRecord> Records => Set<TrackingRecord>();
    public DbSet<TrackingEntry> Entries => Set<TrackingEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<TrackingRecord>(entity =>
        {
            entity.ToTable("tracking_records");
            entity.HasKey(value => value.ShipmentId);
            entity.Property(value => value.ShipmentId).ValueGeneratedNever();
            entity.Property(value => value.TrackingNumber).HasMaxLength(26);
            entity.HasIndex(value => value.TrackingNumber).IsUnique();
            entity.Property<uint>("Version").IsRowVersion();
            entity.Ignore(value => value.Timeline);
            entity.Ignore(value => value.CurrentStatus);
            entity.Ignore(value => value.UpdatedAtUtc);
            entity.HasMany(value => value.Entries).WithOne().HasForeignKey("ShipmentId").IsRequired().OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(value => value.Entries).HasField("_entries").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        builder.Entity<TrackingEntry>(entity =>
        {
            entity.ToTable("tracking_entries");
            entity.HasKey(value => value.EventId);
            entity.Property(value => value.EventId).ValueGeneratedNever();
            entity.Property(value => value.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(value => value.FacilityCode).HasMaxLength(40);
            entity.HasIndex("ShipmentId", nameof(TrackingEntry.OccurredAtUtc));
        });
        builder.AddInboxStateEntity();
        builder.AddOutboxMessageEntity();
        builder.AddOutboxStateEntity();
    }
}
