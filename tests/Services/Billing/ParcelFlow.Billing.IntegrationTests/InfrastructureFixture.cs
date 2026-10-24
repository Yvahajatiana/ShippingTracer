using ParcelFlow.Testing;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Xunit;

namespace ParcelFlow.Billing.IntegrationTests;

public sealed class InfrastructureFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:16-alpine").Build();
    public RabbitMqContainer RabbitMq { get; private set; } = null!;
    public RedisContainer Redis { get; } = new RedisBuilder("redis:7-alpine").Build();

    public async Task InitializeAsync()
    {
        // The hosted Shipment booking saga schedules payment timeouts through delayed delivery.
        RabbitMq = await RabbitMqDelayedExchange.BuildAsync(builder =>
            builder.WithUsername("rabbitmq").WithPassword("rabbitmq"));
        await Task.WhenAll(Postgres.StartAsync(), RabbitMq.StartAsync(), Redis.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Redis.DisposeAsync();
        await RabbitMq.DisposeAsync();
        await Postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "Billing-infrastructure";
}
