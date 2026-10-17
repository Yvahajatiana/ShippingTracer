using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParcelFlow.Warehouse.Domain;
using ParcelFlow.Warehouse.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Warehouse.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceTests(InfrastructureFixture infrastructure)
{
    private WarehouseDbContext CreateContext() => new(new DbContextOptionsBuilder<WarehouseDbContext>()
        .UseNpgsql(infrastructure.Postgres.GetConnectionString()).Options);
    internal static string Number() => "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
    internal static WarehouseSite Site() => new(Guid.NewGuid(), "FR-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
        "Entrepôt de test", WarehouseKind.Warehouse, "18 avenue Tony Garnier", "Lyon", "69007", "FR");

    [Fact]
    public async Task Migration_roundtrip_preserves_stock_scans_and_timestamp_precision()
    {
        await using var database = CreateContext();
        await database.Database.MigrateAsync();
        var site = Site();
        var inventory = new ParcelInventory(Guid.NewGuid(), Number());
        inventory.ApplyScan(site, ScanDirection.Entry, DateTimeOffset.UtcNow);
        var scan = new WarehouseScan(Guid.NewGuid(), inventory.ShipmentId, site.Id, inventory.TrackingNumber,
            ScanDirection.Entry, inventory.LastScanAtUtc!.Value, "operator-test");
        database.AddRange(site, inventory, scan);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var restored = await database.Inventory.SingleAsync(x => x.ShipmentId == inventory.ShipmentId);
        restored.CurrentWarehouseId.Should().Be(site.Id);
        restored.LastScanAtUtc.Should().Be(scan.OccurredAtUtc);
        restored.HasBeenPickedUp.Should().BeTrue();
        (await database.Scans.SingleAsync(x => x.ScanId == scan.ScanId)).OperatorSubject.Should().Be("operator-test");
        (await database.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Foreign_keys_protect_stock_and_scan_audit_history()
    {
        await using var database = CreateContext();
        await database.Database.MigrateAsync();
        var site = Site();
        var inventory = new ParcelInventory(Guid.NewGuid(), Number());
        inventory.ApplyScan(site, ScanDirection.Entry, DateTimeOffset.UtcNow);
        database.AddRange(site, inventory);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        database.Warehouses.Remove(await database.Warehouses.SingleAsync(x => x.Id == site.Id));
        var failure = await FluentActions.Awaiting(() => database.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        failure.Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        database.ChangeTracker.Clear();
        database.Scans.Add(new WarehouseScan(Guid.NewGuid(), Guid.NewGuid(), site.Id, Number(), ScanDirection.Entry, DateTimeOffset.UtcNow, "operator"));
        await FluentActions.Awaiting(() => database.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Scan_identifiers_are_globally_unique_and_cannot_be_reused_for_another_parcel()
    {
        await using var database = CreateContext();
        await database.Database.MigrateAsync();
        var site = Site();
        var first = new ParcelInventory(Guid.NewGuid(), Number());
        var second = new ParcelInventory(Guid.NewGuid(), Number());
        var id = Guid.NewGuid();
        database.AddRange(site, first, second, new WarehouseScan(id, first.ShipmentId, site.Id, first.TrackingNumber, ScanDirection.Entry, DateTimeOffset.UtcNow, "operator"));
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        database.Scans.Add(new WarehouseScan(id, second.ShipmentId, site.Id, second.TrackingNumber, ScanDirection.Exit, DateTimeOffset.UtcNow, "operator"));
        var failure = await FluentActions.Awaiting(() => database.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        failure.Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Inventory_xmin_rejects_stale_writers_without_overwriting_stock()
    {
        await using var first = CreateContext();
        await first.Database.MigrateAsync();
        var site = Site();
        var parcel = new ParcelInventory(Guid.NewGuid(), Number());
        first.AddRange(site, parcel);
        await first.SaveChangesAsync();
        await using var second = CreateContext();
        var stale = await second.Inventory.SingleAsync(x => x.ShipmentId == parcel.ShipmentId);
        parcel.ApplyScan(site, ScanDirection.Entry, DateTimeOffset.UtcNow);
        await first.SaveChangesAsync();
        stale.ApplyScan(site, ScanDirection.Entry, DateTimeOffset.UtcNow);
        await FluentActions.Awaiting(() => second.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
