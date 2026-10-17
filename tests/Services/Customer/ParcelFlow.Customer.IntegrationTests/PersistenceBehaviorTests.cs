using FluentAssertions;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.BuildingBlocks.Contracts.Customer.V1;
using ParcelFlow.Customer.Domain;
using ParcelFlow.Customer.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Customer.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceBehaviorTests(InfrastructureFixture infrastructure)
{
    private CustomerDbContext CreateContext() => new(new DbContextOptionsBuilder<CustomerDbContext>()
        .UseNpgsql(infrastructure.Postgres.GetConnectionString()).Options);

    [Fact]
    public async Task Concurrent_updates_are_detected_by_postgres_row_version()
    {
        await using var first = CreateContext();
        await first.Database.MigrateAsync();
        var profile = new CustomerProfile(Guid.NewGuid().ToString(), "Alice", "Martin", "alice@example.test", null);
        first.Customers.Add(profile);
        await first.SaveChangesAsync();
        await using var second = CreateContext();
        var stale = await second.Customers.SingleAsync(value => value.Id == profile.Id);
        profile.Update("Alice", "Durand", "alice@example.test", null);
        await first.SaveChangesAsync();
        stale.Update("Alice", "Bernard", "alice@example.test", null);
        var save = async () => await second.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Failed_business_write_does_not_commit_its_outbox_event()
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
        var subject = Guid.NewGuid().ToString();
        context.Customers.Add(new CustomerProfile(subject, "Alice", "Martin", "alice@example.test", null));
        await context.SaveChangesAsync();
        var duplicate = new CustomerProfile(subject, "Bob", "Martin", "bob@example.test", null);
        var message = new CustomerProfileCreated(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, duplicate.Id, subject);
        context.Customers.Add(duplicate);
        await scope.ServiceProvider.GetRequiredService<IPublishEndpoint>().Publish(message, send => send.MessageId = message.EventId);
        var save = async () => await context.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateException>();

        await using var verify = CreateContext();
        (await verify.Customers.AnyAsync(value => value.Id == duplicate.Id)).Should().BeFalse();
        (await verify.Set<OutboxMessage>().AnyAsync(value => value.MessageId == message.EventId)).Should().BeFalse();
    }

    [Fact]
    public async Task Explicit_demo_seed_is_repeatable_without_duplicates()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:SeedDemoData"] = "true"
        }).Build());
        services.AddDbContext<CustomerDbContext>(options => options.UseNpgsql(infrastructure.Postgres.GetConnectionString()));
        await using var provider = services.BuildServiceProvider();
        await provider.InitializeCustomerDatabaseAsync();
        await provider.InitializeCustomerDatabaseAsync();

        await using var verify = CreateContext();
        (await verify.Companies.CountAsync(value => value.RegistrationNumber == "DEMO-FR-001")).Should().Be(1);
        (await verify.Customers.CountAsync(value => value.KeycloakSubject == "10000000-0000-0000-0000-000000000001")).Should().Be(1);
        var customer = await verify.Customers.Include(value => value.Addresses)
            .SingleAsync(value => value.KeycloakSubject == "10000000-0000-0000-0000-000000000001");
        customer.Addresses.Should().ContainSingle();
        customer.CompanyId.Should().NotBeNull();
    }
}
