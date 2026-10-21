using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.Billing.Domain;

namespace ParcelFlow.Billing.Infrastructure.Persistence;

public static class DatabaseInitialization
{
    public static async Task InitializeBillingDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
        if (!bool.TryParse(scope.ServiceProvider.GetRequiredService<IConfiguration>()["Database:SeedDemoData"], out var seed) || !seed) return;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var at = new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
        foreach (var (id, subject, amount) in new[]
        {
            (Guid.Parse("71000000-0000-0000-0000-000000000001"), "10000000-0000-0000-0000-000000000001", 12.90m),
            (Guid.Parse("71000000-0000-0000-0000-000000000002"), "10000000-0000-0000-0000-000000000002", 18.50m)
        })
        {
            var key = "billing:invoice:" + id;
            await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
            if (!await database.Invoices.AnyAsync(x => x.Id == id, cancellationToken))
                database.Invoices.Add(new Invoice(id, subject, amount, "EUR", at));
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
