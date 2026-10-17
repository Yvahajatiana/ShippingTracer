using FluentAssertions;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.Shipment.Domain;
using ParcelFlow.Shipment.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Shipment.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceTests(InfrastructureFixture infrastructure)
{
    private ShipmentDbContext CreateContext() => new(new DbContextOptionsBuilder<ShipmentDbContext>()
        .UseNpgsql(infrastructure.Postgres.GetConnectionString()).Options);

    internal static ParcelShipment CreateShipment(string? trackingNumber = null) => new("demo-customer",
        trackingNumber ?? "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant(),
        new ShippingAddress("Emma Martin", "emma@example.test", "+33600000001", "12 rue des Peupliers", null, "69003", "Lyon", "FR"),
        new ShippingAddress("Lucas Bernard", "lucas@example.test", "+33600000002", "28 rue des Tilleuls", null, "75012", "Paris", "FR"),
        new ParcelSpecification(2, 20, 15, 10), 7.30m, DeliveryZone.Domestic);

    [Fact]
    public async Task Migrations_initialize_reference_tariffs_without_demo_seed_and_preserve_admin_updates()
    {
        var connection = new NpgsqlConnectionStringBuilder(infrastructure.Postgres.GetConnectionString())
        {
            Database = $"shipment_tariffs_{Guid.NewGuid():N}"
        };
        await using var database = new ShipmentDbContext(new DbContextOptionsBuilder<ShipmentDbContext>()
            .UseNpgsql(connection.ConnectionString).Options);
        // No application initialization or demo seed flag: migrations provide the business reference data.
        await database.Database.MigrateAsync();
        var tariffs = await database.Tariffs.ToListAsync();
        tariffs.Should().HaveCount(3);
        tariffs.Should().ContainSingle(value => value.Zone == DeliveryZone.Domestic && value.BasePrice == 4.90m && value.PricePerKg == 1.20m);
        tariffs.Should().ContainSingle(value => value.Zone == DeliveryZone.Europe && value.BasePrice == 9.90m && value.PricePerKg == 2.50m);
        tariffs.Should().ContainSingle(value => value.Zone == DeliveryZone.International && value.BasePrice == 19.90m && value.PricePerKg == 5m);
        var domestic = tariffs.Single(value => value.Zone == DeliveryZone.Domestic);
        domestic.Update(8.50m, 2.25m);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        await database.Database.MigrateAsync();
        var updated = await database.Tariffs.SingleAsync(value => value.Zone == DeliveryZone.Domestic);
        updated.BasePrice.Should().Be(8.50m);
        updated.PricePerKg.Should().Be(2.25m);
        (await database.Shipments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Reference_data_migration_preserves_tariffs_already_created_by_previous_demo_initialization()
    {
        var connection = new NpgsqlConnectionStringBuilder(infrastructure.Postgres.GetConnectionString())
        {
            Database = $"shipment_upgrade_{Guid.NewGuid():N}"
        };
        await using var database = new ShipmentDbContext(new DbContextOptionsBuilder<ShipmentDbContext>()
            .UseNpgsql(connection.ConnectionString).Options);
        await database.GetService<IMigrator>().MigrateAsync("20261017202719_InitialShipment");
        database.Tariffs.Add(new Tariff(DeliveryZone.Domestic, 8.50m, 2.25m));
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        await database.Database.MigrateAsync();
        (await database.Tariffs.CountAsync()).Should().Be(3);
        var domestic = await database.Tariffs.SingleAsync(value => value.Zone == DeliveryZone.Domestic);
        domestic.BasePrice.Should().Be(8.50m);
        domestic.PricePerKg.Should().Be(2.25m);
        await database.GetService<IMigrator>().MigrateAsync("20261017202719_InitialShipment");
        database.ChangeTracker.Clear();
        (await database.Tariffs.CountAsync()).Should().Be(3);
        await database.Database.MigrateAsync();
        var preserved = await database.Tariffs.SingleAsync(value => value.Zone == DeliveryZone.Domestic);
        preserved.BasePrice.Should().Be(8.50m);
        preserved.PricePerKg.Should().Be(2.25m);
    }

    [Fact]
    public async Task Migration_roundtrip_preserves_address_and_parcel_snapshots()
    {
        await using var database = CreateContext();
        await database.Database.MigrateAsync();
        var shipment = CreateShipment();
        database.Shipments.Add(shipment);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var restored = await database.Shipments.SingleAsync(value => value.Id == shipment.Id);
        restored.Sender.City.Should().Be("Lyon");
        restored.Recipient.City.Should().Be("Paris");
        restored.Parcel.WeightKg.Should().Be(2);
        restored.Status.Should().Be(ShipmentStatus.AwaitingPayment);
        (await database.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Tracking_number_is_unique()
    {
        await using var database = CreateContext();
        await database.Database.MigrateAsync();
        var first = CreateShipment();
        database.Shipments.Add(first);
        await database.SaveChangesAsync();
        database.Shipments.Add(CreateShipment(first.TrackingNumber));
        var save = async () => await database.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Postgres_row_version_detects_concurrent_payment_updates()
    {
        await using var first = CreateContext();
        await first.Database.MigrateAsync();
        var shipment = CreateShipment();
        first.Shipments.Add(shipment);
        await first.SaveChangesAsync();
        await using var second = CreateContext();
        var stale = await second.Shipments.SingleAsync(value => value.Id == shipment.Id);
        shipment.MarkPaid();
        await first.SaveChangesAsync();
        stale.MarkPaymentFailed();
        var save = async () => await second.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Duplicate_business_write_rolls_back_its_outbox_event()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ShipmentDbContext>(options => options.UseNpgsql(infrastructure.Postgres.GetConnectionString()));
        services.AddMassTransit(registration =>
        {
            registration.AddEntityFrameworkOutbox<ShipmentDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
            });
            registration.UsingRabbitMq((_, bus) => bus.Host(new Uri(infrastructure.RabbitMq.GetConnectionString())));
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ShipmentDbContext>();
        await database.Database.MigrateAsync();
        var first = CreateShipment();
        database.Shipments.Add(first);
        await database.SaveChangesAsync();
        var duplicate = CreateShipment(first.TrackingNumber);
        database.Shipments.Add(duplicate);
        var message = new ShipmentCreated(Guid.NewGuid(), duplicate.Id, DateTimeOffset.UtcNow, duplicate.Id,
            duplicate.TrackingNumber, duplicate.OwnerSubject, duplicate.Price, duplicate.Currency);
        await scope.ServiceProvider.GetRequiredService<IPublishEndpoint>().Publish(message, send => send.MessageId = message.EventId);
        var save = async () => await database.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateException>();
        await using var verify = CreateContext();
        (await verify.Shipments.AnyAsync(value => value.Id == duplicate.Id)).Should().BeFalse();
        (await verify.Set<OutboxMessage>().AnyAsync(value => value.MessageId == message.EventId)).Should().BeFalse();
    }
}
