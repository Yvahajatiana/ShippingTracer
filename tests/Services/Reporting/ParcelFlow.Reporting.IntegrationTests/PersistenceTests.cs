using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ParcelFlow.Reporting.Application;
using ParcelFlow.Reporting.Domain;
using ParcelFlow.Reporting.Infrastructure;
using ParcelFlow.Reporting.Infrastructure.Persistence;
using Xunit;
namespace ParcelFlow.Reporting.IntegrationTests;
internal static class ConsumerTestHost
{
    public static ServiceProvider Create(InfrastructureFixture infrastructure, string? databaseName = null, Action<IServiceCollection>? configure = null)
    {
        var connection = new NpgsqlConnectionStringBuilder(infrastructure.Postgres.GetConnectionString());
        if (databaseName is not null) connection.Database = databaseName;
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ReportingDatabase"] = connection.ConnectionString,
            ["ConnectionStrings:Redis"] = infrastructure.Redis.GetConnectionString(),
            ["RabbitMq:Host"] = infrastructure.RabbitMq.Hostname,
            ["RabbitMq:Port"] = infrastructure.RabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:Username"] = "rabbitmq", ["RabbitMq:Password"] = "rabbitmq"
        }).Build();
        var services = new ServiceCollection(); services.AddSingleton<IConfiguration>(settings); services.AddLogging();
        services.AddReportingApplication(); services.AddReportingInfrastructure(settings); configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
    public static async Task Until(Func<Task<bool>> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!await condition()) await Task.Delay(50, deadline.Token);
    }
    public static RecordStatusFact Command(Guid shipmentId, ParcelStatus status, DateTimeOffset time, Guid? source = null) =>
        new(Guid.NewGuid(), source ?? Guid.NewGuid(), shipmentId, "PF" + shipmentId.ToString("N")[..24].ToUpperInvariant(), status, time, null);
}
[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceTests(InfrastructureFixture infrastructure)
{
    [Theory]
    [InlineData("source")] [InlineData("event")] [InlineData("tracking")]
    public async Task Database_enforces_unique_source_event_and_tracking_identities(string constraint)
    {
        await using var provider = ConsumerTestHost.Create(infrastructure); await provider.InitializeReportingDatabaseAsync();
        var id = Guid.NewGuid(); var eventId = Guid.NewGuid(); var sourceId = Guid.NewGuid();
        var tracking = "PF" + id.ToString("N")[..24].ToUpperInvariant();
        using (var scope = provider.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var shipment = new ShipmentMetrics(id, tracking);
            shipment.AddFact(new StatusFact(eventId, sourceId, id, ParcelStatus.Created, DateTimeOffset.UtcNow, null));
            database.Shipments.Add(shipment); await database.SaveChangesAsync();
        }
        using var other = provider.CreateScope(); var db = other.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var secondId = Guid.NewGuid();
        var duplicate = new ShipmentMetrics(secondId, constraint == "tracking" ? tracking : "PF" + secondId.ToString("N")[..24].ToUpperInvariant());
        duplicate.AddFact(new StatusFact(constraint == "event" ? eventId : Guid.NewGuid(), constraint == "source" ? sourceId : Guid.NewGuid(), secondId, ParcelStatus.Created, DateTimeOffset.UtcNow, null));
        db.Shipments.Add(duplicate);
        await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>()
            .Where(x => x.InnerException != null && ((PostgresException)x.InnerException).SqlState == "23505");
    }
    [Fact]
    public async Task Migrations_roundtrip_projection_and_microsecond_normalized_source_fact()
    {
        await using var provider = ConsumerTestHost.Create(infrastructure); await provider.InitializeReportingDatabaseAsync();
        var command = ConsumerTestHost.Command(Guid.NewGuid(), ParcelStatus.Created, DateTimeOffset.UtcNow.AddTicks(7));
        using (var scope = provider.CreateScope()) await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
        using var read = provider.CreateScope(); var database = read.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var entity = await database.Shipments.Include(x => x.Facts).SingleAsync(x => x.ShipmentId == command.ShipmentId);
        entity.Revision.Should().Be(1); entity.CurrentStatus.Should().Be(ParcelStatus.Created);
        entity.CreatedAtUtc.Should().Be(ReportingRules.NormalizeTimestamp(command.OccurredAtUtc));
        entity.Facts.Single().Matches(command.SourceEventId, command.ShipmentId, command.Status, command.OccurredAtUtc, null).Should().BeTrue();
        (await database.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }
    [Fact]
    public async Task Explicit_transaction_rollback_removes_root_fact_and_bus_outbox_message_after_save()
    {
        await using var provider = ConsumerTestHost.Create(infrastructure); await provider.InitializeReportingDatabaseAsync();
        var command = ConsumerTestHost.Command(Guid.NewGuid(), ParcelStatus.Created, DateTimeOffset.UtcNow);
        using (var scope = provider.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
            (await database.Facts.AnyAsync(x => x.SourceEventId == command.SourceEventId)).Should().BeTrue();
            await transaction.RollbackAsync();
        }
        using var read = provider.CreateScope(); var db = read.ServiceProvider.GetRequiredService<ReportingDbContext>();
        (await db.Shipments.AnyAsync(x => x.ShipmentId == command.ShipmentId)).Should().BeFalse();
        (await db.Facts.AnyAsync(x => x.SourceEventId == command.SourceEventId)).Should().BeFalse();
        (await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().AnyAsync(x => x.CorrelationId == command.ShipmentId)).Should().BeFalse();
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Conflicting_source_or_envelope_is_rejected_without_modification(bool changeEnvelope)
    {
        await using var provider = ConsumerTestHost.Create(infrastructure); await provider.InitializeReportingDatabaseAsync();
        var command = ConsumerTestHost.Command(Guid.NewGuid(), ParcelStatus.Created, DateTimeOffset.UtcNow);
        using (var scope = provider.CreateScope()) await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
        var conflict = changeEnvelope ? command with { SourceEventId = Guid.NewGuid() } : command with { EventId = Guid.NewGuid(), Status = ParcelStatus.Delivered };
        using (var scope = provider.CreateScope())
            await FluentActions.Awaiting(() => scope.ServiceProvider.GetRequiredService<ISender>().Send(conflict)).Should().ThrowAsync<ReportingConflictException>();
        using var read = provider.CreateScope(); var db = read.ServiceProvider.GetRequiredService<ReportingDbContext>();
        (await db.Facts.CountAsync(x => x.ShipmentId == command.ShipmentId)).Should().Be(1);
    }
    [Fact]
    public async Task Facts_require_persisted_shipment_and_unique_event_identity()
    {
        await using var provider = ConsumerTestHost.Create(infrastructure); await provider.InitializeReportingDatabaseAsync();
        using var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        db.Facts.Add(new StatusFact(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ParcelStatus.Created, DateTimeOffset.UtcNow, null));
        await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>().Where(x => x.InnerException != null && ((PostgresException)x.InnerException).SqlState == "23503");
    }
}

