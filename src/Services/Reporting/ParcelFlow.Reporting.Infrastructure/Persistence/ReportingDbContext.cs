using MassTransit;
using Microsoft.EntityFrameworkCore;
using ParcelFlow.Reporting.Domain;
namespace ParcelFlow.Reporting.Infrastructure.Persistence;
public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : DbContext(options)
{
    public DbSet<ShipmentMetrics> Shipments => Set<ShipmentMetrics>();
    public DbSet<StatusFact> Facts => Set<StatusFact>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<ShipmentMetrics>(entity =>
        {
            entity.ToTable("shipment_metrics"); entity.HasKey(x => x.ShipmentId);
            entity.Property(x => x.ShipmentId).ValueGeneratedNever();
            entity.Property(x => x.TrackingNumber).HasMaxLength(26).IsRequired();
            entity.HasIndex(x => x.TrackingNumber).IsUnique();
            entity.Property(x => x.CurrentStatus).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.DeliverySeconds).HasPrecision(20, 6);
            entity.Property(x => x.TransitSeconds).HasPrecision(20, 6);
            entity.Property<uint>("Version").IsRowVersion();
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasMany(x => x.Facts).WithOne().HasForeignKey(x => x.ShipmentId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(x => x.Facts).HasField("_facts").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        builder.Entity<StatusFact>(entity =>
        {
            entity.ToTable("status_facts"); entity.HasKey(x => x.SourceEventId);
            entity.Property(x => x.SourceEventId).ValueGeneratedNever();
            entity.HasIndex(x => x.EventId).IsUnique();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.FacilityCode).HasMaxLength(50);
            entity.HasIndex(x => new { x.ShipmentId, x.OccurredAtUtc, x.SourceEventId });
        });
        builder.AddInboxStateEntity(); builder.AddOutboxMessageEntity(); builder.AddOutboxStateEntity();
    }
}
