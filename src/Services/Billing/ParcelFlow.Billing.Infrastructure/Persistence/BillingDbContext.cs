using MassTransit;
using Microsoft.EntityFrameworkCore;
using ParcelFlow.Billing.Domain;

namespace ParcelFlow.Billing.Infrastructure.Persistence;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<PaymentAttempt> Payments => Set<PaymentAttempt>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Invoice>(entity =>
        {
            entity.ToTable("billing_invoices");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Ignore(x => x.ShipmentId); entity.Ignore(x => x.Number); entity.Ignore(x => x.RemainingCredit);
            entity.Property(x => x.CustomerSubject).HasMaxLength(200);
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.CreditTotal).HasPrecision(18, 2);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(x => new { x.CustomerSubject, x.IssuedAtUtc, x.Id });
            entity.Property<uint>("Version").IsRowVersion();
        });
        builder.Entity<PaymentAttempt>(entity =>
        {
            entity.ToTable("billing_payments");
            entity.HasKey(x => x.OperationId); entity.Property(x => x.OperationId).ValueGeneratedNever();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.Property(x => x.ActorSubject).HasMaxLength(200);
            entity.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(x => x.InvoiceId).IsUnique();
            entity.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CreditNote>(entity =>
        {
            entity.ToTable("billing_credit_notes");
            entity.HasKey(x => x.Id); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Ignore(x => x.Number);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.Property(x => x.ActorSubject).HasMaxLength(200);
            entity.Property(x => x.Reason).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(x => new { x.InvoiceId, x.OccurredAtUtc, x.Id });
            entity.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        });
        builder.AddInboxStateEntity(); builder.AddOutboxMessageEntity(); builder.AddOutboxStateEntity();
    }
}
