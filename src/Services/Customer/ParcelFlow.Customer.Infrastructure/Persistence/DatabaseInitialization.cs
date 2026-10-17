using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.Customer.Domain;

namespace ParcelFlow.Customer.Infrastructure.Persistence;

public static class DatabaseInitialization
{
    public static async Task InitializeCustomerDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (!bool.TryParse(configuration["Database:SeedDemoData"], out var seed) || !seed)
            return;

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        const string registration = "DEMO-FR-001";
        var company = await database.Companies.SingleOrDefaultAsync(value => value.RegistrationNumber == registration, cancellationToken);
        if (company is null)
        {
            company = new Company("Brume Logistique — Démonstration", registration, "contact@brume.example");
            database.Companies.Add(company);
        }

        await SeedCustomer("10000000-0000-0000-0000-000000000001", "Emma", "Martin", "emma.martin@example.test",
            "+33600000001", "12 rue des Peupliers", "69003", "Lyon", "FR", company.Id);
        await SeedCustomer("10000000-0000-0000-0000-000000000002", "Lucas", "Bernard", "lucas.bernard@example.test",
            "+32400000002", "28 avenue des Tilleuls", "1000", "Bruxelles", "BE", null);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        async Task SeedCustomer(string subject, string firstName, string lastName, string email,
            string phone, string street, string postalCode, string city, string country, Guid? companyId)
        {
            if (await database.Customers.AnyAsync(value => value.KeycloakSubject == subject, cancellationToken))
                return;
            var customer = new CustomerProfile(subject, firstName, lastName, email, phone);
            customer.AssignCompany(companyId);
            customer.AddAddress("Principale", street, null, postalCode, city, country);
            database.Customers.Add(customer);
        }
    }
}
