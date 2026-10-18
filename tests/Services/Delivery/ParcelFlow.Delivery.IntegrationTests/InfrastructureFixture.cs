using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Xunit;

namespace ParcelFlow.Delivery.IntegrationTests;

public sealed class InfrastructureFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:16-alpine").Build();
    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder("rabbitmq:3.13-management-alpine")
        .WithUsername("rabbitmq").WithPassword("rabbitmq").Build();
    public RedisContainer Redis { get; } = new RedisBuilder("redis:7-alpine").Build();

    public async Task InitializeAsync()
    {
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
    public const string Name = "Delivery-infrastructure";
}
