using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ParcelFlow.Delivery.Domain;

namespace ParcelFlow.Delivery.Infrastructure.Persistence;

public sealed class DeliveryDbContext(DbContextOptions<DeliveryDbContext> options, DeliveryProofProtector proofProtector) : DbContext(options)
{
    private static readonly ValueComparer<byte[]?> BytesComparer = new(
        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
        value => value == null ? 0 : value.Length, value => value == null ? null : value.ToArray());

    // The value converters capture the protector: the cached model must be specific to it.
    internal DeliveryProofProtector ProofProtector { get; } = proofProtector;

    public DbSet<DeliveryDriver> Drivers => Set<DeliveryDriver>();
    public DbSet<DeliveryRoute> Routes => Set<DeliveryRoute>();
    public DbSet<DeliveryStop> Stops => Set<DeliveryStop>();
    public DbSet<DeliveryParcel> Parcels => Set<DeliveryParcel>();
    public DbSet<DeliveryAttempt> Attempts => Set<DeliveryAttempt>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // A local, not the property: the cached model must not keep this context instance alive.
        var protector = ProofProtector;
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
            // Personal data is encrypted by the application (GDPR): PostgreSQL only sees ciphertext.
            // EF Core never passes null to a value converter, hence the null-forgiving operators.
            entity.Property(x => x.RecipientName).HasColumnType("bytea").HasConversion(new ValueConverter<string?, byte[]?>(
                value => protector.ProtectText(value!, DeliveryProofField.RecipientName),
                stored => protector.UnprotectText(stored!, DeliveryProofField.RecipientName)));
            entity.Property(x => x.SignaturePng).HasConversion(new ValueConverter<byte[]?, byte[]?>(
                value => protector.Protect(value!, DeliveryProofField.Signature),
                stored => protector.Unprotect(stored!, DeliveryProofField.Signature)), BytesComparer);
            entity.Property(x => x.Photo).HasConversion(new ValueConverter<byte[]?, byte[]?>(
                value => protector.Protect(value!, DeliveryProofField.Photo),
                stored => protector.Unprotect(stored!, DeliveryProofField.Photo)), BytesComparer);
            entity.Property(x => x.PhotoContentType).HasMaxLength(30);
            // Retention job: delivered attempts whose proofs are still stored, oldest first.
            entity.HasIndex(x => x.OccurredAtUtc).HasDatabaseName("IX_delivery_attempts_proof_retention")
                .HasFilter("\"ProofsPurgedAtUtc\" IS NULL AND \"Outcome\" = 'Delivered'");
            entity.HasOne<DeliveryStop>().WithMany().HasForeignKey(x => new { x.RouteId, x.ShipmentId }).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.RouteId, x.ShipmentId }).IsUnique();
        });
        builder.AddInboxStateEntity();
        builder.AddOutboxMessageEntity();
        builder.AddOutboxStateEntity();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, DeliveryModelCacheKeyFactory>();
}

internal sealed class DeliveryModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context is DeliveryDbContext delivery
        ? (context.GetType(), delivery.ProofProtector, designTime)
        : (context.GetType(), designTime);
}
