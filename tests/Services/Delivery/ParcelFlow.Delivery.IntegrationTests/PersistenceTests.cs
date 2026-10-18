using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParcelFlow.Delivery.Domain;
using ParcelFlow.Delivery.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Delivery.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceTests(InfrastructureFixture infrastructure)
{
    private DeliveryDbContext CreateContext() => new(new DbContextOptionsBuilder<DeliveryDbContext>().UseNpgsql(infrastructure.Postgres.GetConnectionString()).Options);
    internal static string Number() => "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
    internal static byte[] Png() => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGNgAAIAAAUAAXpeqz8AAAAASUVORK5CYII=");

    [Fact]
    public async Task Migration_roundtrip_preserves_proof_bytes_stop_relationships_and_microsecond_timestamps()
    {
        await using var database = CreateContext(); await database.Database.MigrateAsync();
        var driver = new DeliveryDriver("driver-" + Guid.NewGuid().ToString("N"), "Camille Martin");
        var route = new DeliveryRoute(Guid.NewGuid(), "Lyon matin", new DateOnly(2026, 10, 18));
        var parcel = new DeliveryParcel(Guid.NewGuid()); parcel.Register(Number()); parcel.MarkPickedUp();
        route.AssignDriver(driver); route.AddParcel(parcel); route.Start(driver, DateTimeOffset.UtcNow);
        var at = parcel.RecordEvent(DateTimeOffset.UtcNow.AddTicks(7));
        route.CompleteStop(parcel.ShipmentId, DeliveryOutcome.Delivered, null, at); parcel.Complete(route.Id, true);
        var attempt = new DeliveryAttempt(Guid.NewGuid(), route.Id, parcel.ShipmentId, DeliveryOutcome.Delivered, at, driver.Subject, "Marie Dupont", null, Png(), Png(), "image/png");
        database.AddRange(driver, route, parcel, attempt); await database.SaveChangesAsync(); database.ChangeTracker.Clear();
        var restored = await database.Attempts.SingleAsync(x => x.OperationId == attempt.OperationId);
        restored.SignaturePng.Should().Equal(Png()); restored.Photo.Should().Equal(Png()); restored.OccurredAtUtc.Should().Be(at);
        restored.PhotoContentType.Should().Be("image/png");
        (await database.Routes.Include(x => x.Stops).SingleAsync(x => x.Id == route.Id)).Stops.Should().ContainSingle(x => x.Status == DeliveryStopStatus.Delivered);
        (await database.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        var delete = () => database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM delivery_stops WHERE \"RouteId\" = {route.Id}");
        var failure = await FluentActions.Awaiting(delete).Should().ThrowAsync<PostgresException>();
        failure.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Required_stop_foreign_key_rejects_orphan_proof()
    {
        await using var database = CreateContext(); await database.Database.MigrateAsync();
        database.Attempts.Add(new DeliveryAttempt(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DeliveryOutcome.Failed, DateTimeOffset.UtcNow, "driver", null, DeliveryFailureReason.RecipientAbsent, null, null, null));
        var failure = await FluentActions.Awaiting(() => database.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        failure.Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Parcel_xmin_rejects_stale_writer_without_erasing_pickup()
    {
        await using var first = CreateContext(); await first.Database.MigrateAsync();
        var parcel = new DeliveryParcel(Guid.NewGuid()); first.Parcels.Add(parcel); await first.SaveChangesAsync();
        await using var second = CreateContext(); var stale = await second.Parcels.SingleAsync(x => x.ShipmentId == parcel.ShipmentId);
        parcel.MarkPickedUp(); await first.SaveChangesAsync(); stale.Register(Number());
        await FluentActions.Awaiting(() => second.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
