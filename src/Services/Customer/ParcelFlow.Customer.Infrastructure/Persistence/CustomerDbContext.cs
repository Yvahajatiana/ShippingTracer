using MassTransit;
using Microsoft.EntityFrameworkCore;
using ParcelFlow.Customer.Domain;

namespace ParcelFlow.Customer.Infrastructure.Persistence;

public sealed class CustomerDbContext(DbContextOptions<CustomerDbContext> options) : DbContext(options)
{
    public DbSet<CustomerProfile> Customers => Set<CustomerProfile>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Address> Addresses => Set<Address>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerProfile>(entity =>
        {
            entity.ToTable("customers");
            entity.HasKey(customer => customer.Id);
            entity.Property(customer => customer.Id).ValueGeneratedNever();
            entity.Property(customer => customer.KeycloakSubject).HasMaxLength(200).IsRequired();
            entity.HasIndex(customer => customer.KeycloakSubject).IsUnique();
            entity.Property(customer => customer.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(customer => customer.LastName).HasMaxLength(100).IsRequired();
            entity.Property(customer => customer.Email).HasMaxLength(254).IsRequired();
            entity.Property(customer => customer.Phone).HasMaxLength(30);
            entity.Property(customer => customer.CreatedAtUtc).IsRequired();
            entity.Property<uint>("Version").IsRowVersion();
            entity.HasOne<Company>().WithMany().HasForeignKey(customer => customer.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(customer => customer.Addresses).WithOne()
                .HasForeignKey(address => address.CustomerId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(customer => customer.Addresses).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<Address>(entity =>
        {
            entity.ToTable("addresses");
            entity.HasKey(address => address.Id);
            entity.Property(address => address.Id).ValueGeneratedNever();
            entity.Property(address => address.Label).HasMaxLength(80).IsRequired();
            entity.Property(address => address.Line1).HasMaxLength(200).IsRequired();
            entity.Property(address => address.Line2).HasMaxLength(200);
            entity.Property(address => address.PostalCode).HasMaxLength(20).IsRequired();
            entity.Property(address => address.City).HasMaxLength(100).IsRequired();
            entity.Property(address => address.CountryCode).HasMaxLength(2).IsRequired();
            entity.Property<uint>("Version").IsRowVersion();
        });
        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("companies");
            entity.HasKey(company => company.Id);
            entity.Property(company => company.Id).ValueGeneratedNever();
            entity.Property(company => company.Name).HasMaxLength(200).IsRequired();
            entity.Property(company => company.RegistrationNumber).HasMaxLength(80).IsRequired();
            entity.HasIndex(company => company.RegistrationNumber).IsUnique();
            entity.Property(company => company.Email).HasMaxLength(254).IsRequired();
            entity.Property<uint>("Version").IsRowVersion();
        });
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
