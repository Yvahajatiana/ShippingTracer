using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Xunit;

namespace ParcelFlow.Reporting.IntegrationTests;

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
        // Regression guard for #26: a host started before its database exists makes MassTransit's outbox
        // delivery service poll it, and PostgreSQL logs each attempt (SQLSTATE 3D000). Creating a database
        // with EF Core accounts for up to three of these lines (existence probes); more means polling.
        var (stdout, stderr) = await Postgres.GetLogsAsync();
        var polled = (stdout + stderr).Split('\n')
            .Select(line => System.Text.RegularExpressions.Regex.Match(line, "FATAL:  database \"([^\"]+)\" does not exist"))
            .Where(match => match.Success).GroupBy(match => match.Groups[1].Value).Where(group => group.Count() > 3)
            .Select(group => $"{group.Key} ({group.Count()})").ToArray();
        await Redis.DisposeAsync();
        await RabbitMq.DisposeAsync();
        await Postgres.DisposeAsync();
        if (polled.Length > 0)
            throw new InvalidOperationException("Databases polled before they existed: " + string.Join(", ", polled));
    }
}

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "Reporting-infrastructure";
}

