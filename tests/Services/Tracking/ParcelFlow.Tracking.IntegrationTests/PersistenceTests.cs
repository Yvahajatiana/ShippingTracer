using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParcelFlow.Tracking.Domain;
using ParcelFlow.Tracking.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Tracking.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceTests(InfrastructureFixture infrastructure)
{
    private TrackingDbContext CreateContext() => new(new DbContextOptionsBuilder<TrackingDbContext>()
        .UseNpgsql(infrastructure.Postgres.GetConnectionString()).Options);

    internal static TrackingRecord CreateRecord(string? number = null, Guid? eventId = null)
    {
        var record = new TrackingRecord(Guid.NewGuid());
        record.Register(number ?? "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant());
        record.AddEntry(new TrackingEntry(eventId ?? Guid.NewGuid(), ParcelStatus.Created, DateTimeOffset.UtcNow, null));
        return record;
    }

    [Fact]
    public async Task Migration_roundtrip_preserves_history_and_tracking_identity()
    {
        await using var database = CreateContext();
        await database.Database.MigrateAsync();
        var record = CreateRecord();
        record.AddEntry(new TrackingEntry(Guid.NewGuid(), ParcelStatus.AtSortingCenter, DateTimeOffset.UtcNow, "FR-LYON-01"));
        database.Records.Add(record);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var restored = await database.Records.Include(x => x.Entries).SingleAsync(x => x.ShipmentId == record.ShipmentId);
        restored.TrackingNumber.Should().Be(record.TrackingNumber);
        restored.Entries.Should().HaveCount(2);
        restored.CurrentStatus.Should().Be(ParcelStatus.AtSortingCenter);
        restored.Entries.Should().ContainSingle(x => x.FacilityCode == "FR-LYON-01");
        (await database.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Registered_number_and_source_event_ids_are_globally_unique()
    {
        await using var database = CreateContext();
        await database.Database.MigrateAsync();
        var record = CreateRecord();
        database.Records.Add(record);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        database.Records.Add(CreateRecord(record.TrackingNumber));
        await FluentActions.Awaiting(() => database.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        database.ChangeTracker.Clear();
        database.Records.Add(CreateRecord(eventId: record.Entries.Single().EventId));
        var exception = await FluentActions.Awaiting(() => database.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        exception.Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Appending_an_entry_updates_root_version_and_rejects_stale_writers_atomically()
    {
        await using var first = CreateContext();
        await first.Database.MigrateAsync();
        var record = CreateRecord();
        first.Records.Add(record);
        await first.SaveChangesAsync();
        await using var second = CreateContext();
        var stale = await second.Records.Include(x => x.Entries).SingleAsync(x => x.ShipmentId == record.ShipmentId);
        record.AddEntry(new TrackingEntry(Guid.NewGuid(), ParcelStatus.InTransit, DateTimeOffset.UtcNow, "FR-LYON-01"));
        await first.SaveChangesAsync();
        stale.AddEntry(new TrackingEntry(Guid.NewGuid(), ParcelStatus.Failed, DateTimeOffset.UtcNow, "FR-PARIS-01"));
        await FluentActions.Awaiting(() => second.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
        second.ChangeTracker.Clear();
        (await second.Entries.CountAsync(x => EF.Property<Guid>(x, "ShipmentId") == record.ShipmentId)).Should().Be(2);
    }
}
