using System.Security.Cryptography;
using System.Text;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Xunit;

namespace ParcelFlow.Shipment.IntegrationTests;

public sealed class InfrastructureFixture : IAsyncLifetime
{
    // Payment timeouts rely on RabbitMQ delayed delivery; the stock image does not ship this plugin.
    private const string DelayedExchangePlugin = "rabbitmq_delayed_message_exchange-3.13.0.ez";
    private const string DelayedExchangePluginUrl =
        "https://github.com/rabbitmq/rabbitmq-delayed-message-exchange/releases/download/v3.13.0/" + DelayedExchangePlugin;
    private const string DelayedExchangePluginSha256 = "3479180df03fa830fd59ccf638a60eee8f0e8261524a8616685522d7b8d9a9ee";

    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:16-alpine").Build();
    public RabbitMqContainer RabbitMq { get; private set; } = null!;
    public RedisContainer Redis { get; } = new RedisBuilder("redis:7-alpine").Build();

    public async Task InitializeAsync()
    {
        var plugin = await DownloadDelayedExchangePlugin();
        RabbitMq = new RabbitMqBuilder("rabbitmq:3.13-management-alpine")
            .WithUsername("rabbitmq").WithPassword("rabbitmq")
            .WithResourceMapping(plugin, $"/opt/rabbitmq/plugins/{DelayedExchangePlugin}")
            .WithResourceMapping(Encoding.ASCII.GetBytes(
                "[rabbitmq_management,rabbitmq_prometheus,rabbitmq_delayed_message_exchange]."), "/etc/rabbitmq/enabled_plugins")
            .Build();
        await Task.WhenAll(Postgres.StartAsync(), RabbitMq.StartAsync(), Redis.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Redis.DisposeAsync();
        await RabbitMq.DisposeAsync();
        await Postgres.DisposeAsync();
    }

    private static async Task<byte[]> DownloadDelayedExchangePlugin()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(1) };
        var plugin = await client.GetByteArrayAsync(new Uri(DelayedExchangePluginUrl));
        // Pin the downloaded archive: an unexpected binary must never be loaded into the broker.
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(plugin)), DelayedExchangePluginSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Delayed message exchange plugin checksum mismatch.");
        return plugin;
    }
}

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "shipment-infrastructure";
}
