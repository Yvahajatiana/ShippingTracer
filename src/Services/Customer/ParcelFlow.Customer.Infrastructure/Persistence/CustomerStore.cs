using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Npgsql;
using ParcelFlow.Customer.Application;
using ParcelFlow.Customer.Domain;
using Polly;

namespace ParcelFlow.Customer.Infrastructure.Persistence;

public sealed class CustomerStore(
    CustomerDbContext database,
    ResiliencePipeline databaseReadPipeline,
    IDistributedCache cache,
    ILogger<CustomerStore> logger) : ICustomerStore
{
    public async Task<CustomerProfile?> FindCustomerAsync(Guid id, CancellationToken cancellationToken) =>
        await databaseReadPipeline.ExecuteAsync(async token =>
            await database.Customers.Include(customer => customer.Addresses)
                .SingleOrDefaultAsync(customer => customer.Id == id, token), cancellationToken);

    public async Task<CustomerProfile?> FindBySubjectAsync(string subject, CancellationToken cancellationToken) =>
        await databaseReadPipeline.ExecuteAsync(async token =>
            await database.Customers.Include(customer => customer.Addresses)
                .SingleOrDefaultAsync(customer => customer.KeycloakSubject == subject, token), cancellationToken);

    public async Task<IReadOnlyList<CustomerProfile>> ListCustomersAsync(int offset, int limit, CancellationToken cancellationToken) =>
        await databaseReadPipeline.ExecuteAsync(async token =>
            await database.Customers.AsNoTracking().Include(customer => customer.Addresses)
                .OrderBy(customer => customer.CreatedAtUtc).ThenBy(customer => customer.Id)
                .Skip(offset).Take(limit).ToListAsync(token), cancellationToken);

    public async Task<Company?> FindCompanyAsync(Guid id, CancellationToken cancellationToken) =>
        await databaseReadPipeline.ExecuteAsync(async token =>
            await database.Companies.SingleOrDefaultAsync(company => company.Id == id, token), cancellationToken);

    public async Task<IReadOnlyList<Company>> ListCompaniesAsync(CancellationToken cancellationToken) =>
        await databaseReadPipeline.ExecuteAsync(async token =>
            await database.Companies.AsNoTracking().OrderBy(company => company.Name).ThenBy(company => company.Id)
                .ToListAsync(token), cancellationToken);

    public void AddCustomer(CustomerProfile profile) => database.Customers.Add(profile);
    public void AddCompany(Company company) => database.Companies.Add(company);
    public void RemoveAddress(Address address) => database.Addresses.Remove(address);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        database.ChangeTracker.DetectChanges();
        var companiesChanged = database.ChangeTracker.Entries<Company>()
            .Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        try
        {
            // Entity changes and MassTransit outbox messages commit in this single transaction.
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CustomerConflictException("The resource was modified concurrently. Reload and retry.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new CustomerConflictException("A profile for this identity or a company with this registration number already exists.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new CustomerConflictException("A referenced resource no longer exists.");
        }

        if (companiesChanged)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
            try
            {
                await cache.RemoveAsync("customer:companies:v1:all", timeout.Token);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A committed business transaction must not be reported as failed on a cache outage.
                logger.LogWarning(exception, "Company cache invalidation failed; entries expire within 60 seconds");
            }
        }
    }
}
