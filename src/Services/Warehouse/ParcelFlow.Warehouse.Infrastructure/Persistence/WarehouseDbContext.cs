using MassTransit;
using Microsoft.EntityFrameworkCore;
using ParcelFlow.Warehouse.Domain;

namespace ParcelFlow.Warehouse.Infrastructure.Persistence;

public sealed class WarehouseDbContext(DbContextOptions<WarehouseDbContext> options) : DbContext(options)
{
    public DbSet<WarehouseSite> Warehouses => Set<WarehouseSite>();
    public DbSet<ParcelInventory> Inventory => Set<ParcelInventory>();
    public DbSet<WarehouseScan> Scans => Set<WarehouseScan>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<WarehouseSite>(entity =>
        {
            entity.ToTable("warehouses");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Code).HasMaxLength(40);
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Street).HasMaxLength(200);
            entity.Property(x => x.City).HasMaxLength(100);
            entity.Property(x => x.PostalCode).HasMaxLength(20);
            entity.Property(x => x.CountryCode).HasMaxLength(2);
            entity.Property<uint>("Version").IsRowVersion();
        });
        builder.Entity<ParcelInventory>(entity =>
        {
            entity.ToTable("parcel_inventory");
            entity.HasKey(x => x.ShipmentId);
            entity.Property(x => x.ShipmentId).ValueGeneratedNever();
            entity.Property(x => x.TrackingNumber).HasMaxLength(26);
            entity.HasIndex(x => x.TrackingNumber).IsUnique();
            entity.HasIndex(x => new { x.CurrentWarehouseId, x.TrackingNumber });
            entity.HasOne<WarehouseSite>().WithMany().HasForeignKey(x => x.CurrentWarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.Property<uint>("Version").IsRowVersion();
        });
        builder.Entity<WarehouseScan>(entity =>
        {
            entity.ToTable("warehouse_scans");
            entity.HasKey(x => x.ScanId);
            entity.Property(x => x.ScanId).ValueGeneratedNever();
            entity.Property(x => x.TrackingNumber).HasMaxLength(26);
            entity.Property(x => x.OperatorSubject).HasMaxLength(200);
            entity.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
            entity.HasOne<WarehouseSite>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ParcelInventory>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ShipmentId, x.OccurredAtUtc });
        });
        builder.AddInboxStateEntity();
        builder.AddOutboxMessageEntity();
        builder.AddOutboxStateEntity();
    }
}
