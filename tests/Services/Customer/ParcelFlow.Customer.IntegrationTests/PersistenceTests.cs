using FluentAssertions;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.BuildingBlocks.Contracts.Customer.V1;
using ParcelFlow.Customer.Domain;
using ParcelFlow.Customer.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Customer.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceTests(InfrastructureFixture infrastructure)
{
    private CustomerDbContext CreateContext() => new(new DbContextOptionsBuilder<CustomerDbContext>()
        .UseNpgsql(infrastructure.Postgres.GetConnectionString()).Options);

    [Fact]
    public async Task Migration_creates_schema_and_enforces_unique_subject()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        var subject = Guid.NewGuid().ToString();
        context.Customers.Add(new CustomerProfile(subject, "Marie", "Martin", "marie@example.test", null));
        await context.SaveChangesAsync();
        context.Customers.Add(new CustomerProfile(subject, "Paul", "Martin", "paul@example.test", null));
        var duplicate = async () => await context.SaveChangesAsync();
        await duplicate.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Company_registration_is_unique_and_customer_company_must_exist()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var registration = Guid.NewGuid().ToString();
        context.Companies.Add(new Company("Atelier Martin", registration, "contact@example.test"));
        await context.SaveChangesAsync();
        context.Companies.Add(new Company("Other", registration, "other@example.test"));
        var duplicate = async () => await context.SaveChangesAsync();
        await duplicate.Should().ThrowAsync<DbUpdateException>();
        context.ChangeTracker.Clear();
        var customer = new CustomerProfile(Guid.NewGuid().ToString(), "Marie", "Martin", "marie@example.test", null);
        customer.AssignCompany(Guid.NewGuid());
        context.Customers.Add(customer);
        var orphan = async () => await context.SaveChangesAsync();
        await orphan.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Customer_and_event_are_committed_atomically_to_database_outbox()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<CustomerDbContext>(options => options.UseNpgsql(infrastructure.Postgres.GetConnectionString()));
        services.AddMassTransit(registration =>
        {
            registration.AddEntityFrameworkOutbox<CustomerDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
            });
            registration.UsingRabbitMq((_, bus) => bus.Host(new Uri(infrastructure.RabbitMq.GetConnectionString())));
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
        await context.Database.MigrateAsync();
        var customer = new CustomerProfile(Guid.NewGuid().ToString(), "Marie", "Martin", "marie@example.test", null);
        var message = new CustomerProfileCreated(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, customer.Id, customer.KeycloakSubject);
        context.Customers.Add(customer);
        await scope.ServiceProvider.GetRequiredService<IPublishEndpoint>().Publish(message, published => published.MessageId = message.EventId);
        await context.SaveChangesAsync();

        await using var verify = CreateContext();
        (await verify.Customers.AnyAsync(profile => profile.Id == customer.Id)).Should().BeTrue();
        (await verify.Set<OutboxMessage>().SingleAsync(stored => stored.MessageId == message.EventId))
            .Body.Should().Contain(customer.Id.ToString());
    }
}
