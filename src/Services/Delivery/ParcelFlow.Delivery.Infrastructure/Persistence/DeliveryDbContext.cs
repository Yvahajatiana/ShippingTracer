using MassTransit;
using Microsoft.EntityFrameworkCore;
using ParcelFlow.Delivery.Domain;

namespace ParcelFlow.Delivery.Infrastructure.Persistence;

public sealed class DeliveryDbContext(DbContextOptions<DeliveryDbContext> options) : DbContext(options)
{
    public DbSet<DeliveryDriver> Drivers => Set<DeliveryDriver>();
    public DbSet<DeliveryRoute> Routes => Set<DeliveryRoute>();
    public DbSet<DeliveryStop> Stops => Set<DeliveryStop>();
    public DbSet<DeliveryParcel> Parcels => Set<DeliveryParcel>();
    public DbSet<DeliveryAttempt> Attempts => Set<DeliveryAttempt>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<DeliveryDriver>(entity =>
        {
            entity.ToTable("delivery_drivers");
            entity.HasKey(x => x.Subject);
            entity.Property(x => x.Subject).HasMaxLength(200).ValueGeneratedNever();
            entity.Property(x => x.DisplayName).HasMaxLength(120);
            entity.Property<uint>("Version").IsRowVersion();
        });
        builder.Entity<DeliveryRoute>(entity =>
        {
            entity.ToTable("delivery_routes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.DriverSubject).HasMaxLength(200);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.HasOne<DeliveryDriver>().WithMany().HasForeignKey(x => x.DriverSubject).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Stops).WithOne().HasForeignKey(x => x.RouteId).IsRequired().OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Stops).HasField("_stops").UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasIndex(x => new { x.ServiceDate, x.Id });
            entity.HasIndex(x => new { x.DriverSubject, x.Status });
            entity.Property<uint>("Version").IsRowVersion();
        });
        builder.Entity<DeliveryParcel>(entity =>
        {
            entity.ToTable("delivery_parcels");
            entity.HasKey(x => x.ShipmentId);
            entity.Property(x => x.ShipmentId).ValueGeneratedNever();
            entity.Property(x => x.TrackingNumber).HasMaxLength(26);
            entity.HasIndex(x => x.TrackingNumber).IsUnique();
            entity.HasOne<DeliveryRoute>().WithMany().HasForeignKey(x => x.ActiveRouteId).OnDelete(DeleteBehavior.Restrict);
            entity.Property<uint>("Version").IsRowVersion();
        });
        builder.Entity<DeliveryStop>(entity =>
        {
            entity.ToTable("delivery_stops");
            entity.HasKey(x => new { x.RouteId, x.ShipmentId });
            entity.Property(x => x.RouteId).ValueGeneratedNever();
            entity.Property(x => x.ShipmentId).ValueGeneratedNever();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.FailureReason).HasConversion<string>().HasMaxLength(40);
            entity.HasOne<DeliveryParcel>().WithMany().HasForeignKey(x => x.ShipmentId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<DeliveryAttempt>(entity =>
        {
            entity.ToTable("delivery_attempts");
            entity.HasKey(x => x.OperationId);
            entity.Property(x => x.OperationId).ValueGeneratedNever();
            entity.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.FailureReason).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.ActorSubject).HasMaxLength(200);
            entity.Property(x => x.RecipientName).HasMaxLength(120);
            entity.Property(x => x.PhotoContentType).HasMaxLength(30);
            entity.HasOne<DeliveryStop>().WithMany().HasForeignKey(x => new { x.RouteId, x.ShipmentId }).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.RouteId, x.ShipmentId }).IsUnique();
        });
        builder.AddInboxStateEntity();
        builder.AddOutboxMessageEntity();
        builder.AddOutboxStateEntity();
    }
}
