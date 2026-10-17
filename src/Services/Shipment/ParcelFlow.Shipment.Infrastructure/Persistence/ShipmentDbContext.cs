using MassTransit;
using Microsoft.EntityFrameworkCore;
using ParcelFlow.Shipment.Domain;
using ParcelFlow.Shipment.Infrastructure.Booking;

namespace ParcelFlow.Shipment.Infrastructure.Persistence;

public sealed class ShipmentDbContext(DbContextOptions<ShipmentDbContext> options) : DbContext(options)
{
    public DbSet<ParcelShipment> Shipments => Set<ParcelShipment>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<ShipmentBookingState> Bookings => Set<ShipmentBookingState>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<ParcelShipment>(entity =>
        {
            entity.ToTable("shipments");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).ValueGeneratedNever();
            entity.Property(value => value.OwnerSubject).HasMaxLength(200).IsRequired();
            entity.Property(value => value.TrackingNumber).HasMaxLength(40).IsRequired();
            entity.HasIndex(value => value.TrackingNumber).IsUnique();
            entity.HasIndex(value => new { value.OwnerSubject, value.CreatedAtUtc });
            entity.Property(value => value.Price).HasPrecision(18, 2);
            entity.Ignore(value => value.Currency);
            entity.Property(value => value.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(value => value.Zone).HasConversion<string>().HasMaxLength(20);
            entity.Property<uint>("Version").IsRowVersion();
            entity.OwnsOne(value => value.Sender, ConfigureAddress);
            entity.OwnsOne(value => value.Recipient, ConfigureAddress);
            entity.Navigation(value => value.Sender).IsRequired();
            entity.Navigation(value => value.Recipient).IsRequired();
            entity.OwnsOne(value => value.Parcel, parcel =>
            {
                parcel.Property(value => value.WeightKg).HasPrecision(10, 3);
                parcel.Property(value => value.LengthCm).HasPrecision(10, 2);
                parcel.Property(value => value.WidthCm).HasPrecision(10, 2);
                parcel.Property(value => value.HeightCm).HasPrecision(10, 2);
                parcel.Ignore(value => value.BillableWeightKg);
            });
            entity.Navigation(value => value.Parcel).IsRequired();
        });
        builder.Entity<Tariff>(entity =>
        {
            entity.ToTable("tariffs");
            entity.HasKey(value => value.Zone);
            entity.Property(value => value.Zone).HasConversion<string>().HasMaxLength(20).ValueGeneratedNever();
            entity.Property(value => value.BasePrice).HasPrecision(18, 2);
            entity.Property(value => value.PricePerKg).HasPrecision(18, 2);
            entity.Property<uint>("Version").IsRowVersion();
            entity.Ignore(value => value.Currency);
            entity.HasData(
                new Tariff(DeliveryZone.Domestic, 4.90m, 1.20m),
                new Tariff(DeliveryZone.Europe, 9.90m, 2.50m),
                new Tariff(DeliveryZone.International, 19.90m, 5m));
        });
        builder.Entity<ShipmentBookingState>(entity =>
        {
            entity.ToTable("shipment_bookings");
            entity.HasKey(value => value.CorrelationId);
            entity.Property(value => value.CorrelationId).ValueGeneratedNever();
            entity.Property(value => value.CurrentState).HasMaxLength(40);
            entity.Property(value => value.TrackingNumber).HasMaxLength(40);
            entity.Property(value => value.Currency).HasMaxLength(3);
            entity.Property(value => value.Amount).HasPrecision(18, 2);
            entity.Property(value => value.RowVersion).IsRowVersion();
        });
        builder.AddInboxStateEntity();
        builder.AddOutboxMessageEntity();
        builder.AddOutboxStateEntity();
    }

    private static void ConfigureAddress(Microsoft.EntityFrameworkCore.Metadata.Builders.OwnedNavigationBuilder<ParcelShipment, ShippingAddress> address)
    {
        address.Property(value => value.Name).HasMaxLength(200).IsRequired();
        address.Property(value => value.Email).HasMaxLength(254).IsRequired();
        address.Property(value => value.Phone).HasMaxLength(30).IsRequired();
        address.Property(value => value.Line1).HasMaxLength(200).IsRequired();
        address.Property(value => value.Line2).HasMaxLength(200);
        address.Property(value => value.PostalCode).HasMaxLength(20).IsRequired();
        address.Property(value => value.City).HasMaxLength(100).IsRequired();
        address.Property(value => value.CountryCode).HasMaxLength(2).IsRequired();
    }
}
