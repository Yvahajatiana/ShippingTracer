using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ParcelFlow.Delivery.Application;
using ParcelFlow.Delivery.Domain;
using ParcelFlow.Delivery.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Delivery.IntegrationTests;

public sealed class ProofProtectorTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static DeliveryProofProtector Protector(string key, params string[] previous) => new(new(key, previous, 90, TimeSpan.FromHours(1)));

    [Fact]
    public void Protected_value_round_trips_and_hides_the_plaintext()
    {
        var protector = TestProofKeys.Protector;
        var stored = protector.ProtectText("Marie Dupont", DeliveryProofField.RecipientName);
        DeliveryProofProtector.IsProtected(stored).Should().BeTrue();
        Encoding.UTF8.GetString(stored).Should().NotContain("Marie");
        protector.UnprotectText(stored, DeliveryProofField.RecipientName).Should().Be("Marie Dupont");
        protector.ProtectText("Marie Dupont", DeliveryProofField.RecipientName).Should().NotEqual(stored, "each value gets its own nonce");
    }

    [Fact]
    public void Value_cannot_be_moved_to_another_field_nor_tampered_with()
    {
        var protector = TestProofKeys.Protector;
        var signature = protector.Protect(PersistenceTests.Png(), DeliveryProofField.Signature);
        FluentActions.Invoking(() => protector.Unprotect(signature, DeliveryProofField.Photo)).Should().Throw<CryptographicException>();
        signature[^1] ^= 1;
        FluentActions.Invoking(() => protector.Unprotect(signature, DeliveryProofField.Signature)).Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Previous_keys_keep_decrypting_after_a_rotation()
    {
        var oldKey = NewKey();
        var stored = Protector(oldKey).Protect(PersistenceTests.Png(), DeliveryProofField.Photo);
        Protector(NewKey(), oldKey).Unprotect(stored, DeliveryProofField.Photo).Should().Equal(PersistenceTests.Png());
        FluentActions.Invoking(() => Protector(NewKey()).Unprotect(stored, DeliveryProofField.Photo))
            .Should().Throw<CryptographicException>().WithMessage("*PreviousEncryptionKeys*");
    }

    [Fact]
    public void Legacy_plaintext_is_returned_unchanged()
    {
        TestProofKeys.Protector.Unprotect(PersistenceTests.Png(), DeliveryProofField.Signature).Should().Equal(PersistenceTests.Png());
        TestProofKeys.Protector.UnprotectText(Encoding.UTF8.GetBytes("Jean Martin"), DeliveryProofField.RecipientName).Should().Be("Jean Martin");
    }

    [Theory]
    [InlineData(null, null, "*EncryptionKey is required*")]
    [InlineData("c2hvcnQ=", null, "*exactly 32 bytes*")]
    [InlineData("not base64!", null, "*exactly 32 bytes*")]
    [InlineData(TestProofKeys.EncryptionKey, "0", "*RetentionDays*")]
    [InlineData(TestProofKeys.EncryptionKey, "3651", "*RetentionDays*")]
    [InlineData(TestProofKeys.EncryptionKey, "ninety", "*RetentionDays*")]
    public void Invalid_settings_are_rejected(string? key, string? retentionDays, string message)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DeliveryProofs:EncryptionKey"] = key, ["DeliveryProofs:RetentionDays"] = retentionDays
        }).Build();
        FluentActions.Invoking(() => new DeliveryProofProtector(DeliveryProofSettings.Load(configuration)))
            .Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    [Fact]
    public void Settings_default_to_ninety_days_and_read_previous_keys()
    {
        var previous = NewKey();
        var settings = DeliveryProofSettings.Load(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DeliveryProofs:EncryptionKey"] = NewKey(), ["DeliveryProofs:PreviousEncryptionKeys"] = $" {previous} , "
        }).Build());
        settings.RetentionDays.Should().Be(90);
        settings.PreviousEncryptionKeys.Should().Equal(previous);
    }
}

[Collection(InfrastructureCollection.Name)]
public sealed class ProofRetentionTests(InfrastructureFixture infrastructure)
{
    private DeliveryDbContext CreateContext() => new(new DbContextOptionsBuilder<DeliveryDbContext>().UseNpgsql(infrastructure.Postgres.GetConnectionString()).Options, TestProofKeys.Protector);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private static async Task<DeliveryAttempt> AddAttemptAsync(DeliveryDbContext database, DateTimeOffset occurredAt, DeliveryOutcome outcome = DeliveryOutcome.Delivered)
    {
        var driver = new DeliveryDriver("driver-" + Guid.NewGuid().ToString("N"), "Camille Martin");
        var route = new DeliveryRoute(Guid.NewGuid(), "Lyon matin", new DateOnly(2026, 10, 18));
        var parcel = new DeliveryParcel(Guid.NewGuid()); parcel.Register(PersistenceTests.Number()); parcel.MarkPickedUp();
        route.AssignDriver(driver); route.AddParcel(parcel); route.Start(driver, occurredAt.AddHours(-1));
        var delivered = outcome == DeliveryOutcome.Delivered;
        route.CompleteStop(parcel.ShipmentId, outcome, delivered ? null : DeliveryFailureReason.RecipientAbsent, occurredAt); parcel.Complete(route.Id, delivered);
        var attempt = delivered
            ? new DeliveryAttempt(Guid.NewGuid(), route.Id, parcel.ShipmentId, outcome, occurredAt, driver.Subject, "Marie Dupont", null, PersistenceTests.Png(), PersistenceTests.Png(), "image/png")
            : new DeliveryAttempt(Guid.NewGuid(), route.Id, parcel.ShipmentId, outcome, occurredAt, driver.Subject, null, DeliveryFailureReason.RecipientAbsent, null, null, null);
        database.AddRange(driver, route, parcel, attempt); await database.SaveChangesAsync(); database.ChangeTracker.Clear();
        return attempt;
    }

    private async Task<(byte[]? Name, byte[]? Signature, byte[]? Photo)> RawAsync(Guid operationId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(infrastructure.Postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT \"RecipientName\", \"SignaturePng\", \"Photo\" FROM delivery_attempts WHERE \"OperationId\" = @id", connection);
        command.Parameters.AddWithValue("id", operationId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        byte[]? Column(int index) => reader.IsDBNull(index) ? null : (byte[])reader.GetValue(index);
        return (Column(0), Column(1), Column(2));
    }

    [Fact]
    public async Task Proofs_are_stored_encrypted_and_read_back_in_clear()
    {
        await using var database = CreateContext(); await database.Database.MigrateAsync();
        var attempt = await AddAttemptAsync(database, DateTimeOffset.UtcNow);
        var raw = await RawAsync(attempt.OperationId);
        foreach (var column in new[] { raw.Name, raw.Signature, raw.Photo }) DeliveryProofProtector.IsProtected(column).Should().BeTrue();
        Encoding.UTF8.GetString(raw.Name!).Should().NotContain("Marie");
        raw.Signature.Should().NotEqual(PersistenceTests.Png());
        var restored = await database.Attempts.SingleAsync(x => x.OperationId == attempt.OperationId);
        restored.RecipientName.Should().Be("Marie Dupont"); restored.SignaturePng.Should().Equal(PersistenceTests.Png()); restored.Photo.Should().Equal(PersistenceTests.Png());
    }

    [Fact]
    public async Task Maintenance_purges_expired_proofs_and_keeps_the_delivery_facts()
    {
        await using var database = CreateContext(); await database.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var expired = await AddAttemptAsync(database, now.AddDays(-31));
        var recent = await AddAttemptAsync(database, now.AddDays(-29));
        var failed = await AddAttemptAsync(database, now.AddDays(-60), DeliveryOutcome.Failed);

        var result = await new DeliveryProofMaintenance(database, TestProofKeys.Settings(retentionDays: 30), new FixedClock(now)).RunAsync(default);

        result.Purged.Should().BeGreaterThanOrEqualTo(1);
        var purged = await database.Attempts.AsNoTracking().SingleAsync(x => x.OperationId == expired.OperationId);
        purged.RecipientName.Should().BeNull(); purged.SignaturePng.Should().BeNull(); purged.Photo.Should().BeNull(); purged.PhotoContentType.Should().BeNull();
        purged.ProofsPurgedAtUtc.Should().Be(DeliveryRules.NormalizeTimestamp(now));
        purged.Outcome.Should().Be(DeliveryOutcome.Delivered); purged.OccurredAtUtc.Should().Be(expired.OccurredAtUtc); purged.ActorSubject.Should().Be(expired.ActorSubject);
        purged.ToDto().Should().BeEquivalentTo(new { HasSignature = false, HasPhoto = false, RecipientName = (string?)null });
        (await RawAsync(expired.OperationId)).Should().Be(((byte[]?)null, (byte[]?)null, (byte[]?)null));

        var kept = await database.Attempts.AsNoTracking().SingleAsync(x => x.OperationId == recent.OperationId);
        kept.RecipientName.Should().Be("Marie Dupont"); kept.ProofsPurgedAtUtc.Should().BeNull();
        (await database.Attempts.AsNoTracking().SingleAsync(x => x.OperationId == failed.OperationId)).ProofsPurgedAtUtc.Should().BeNull();

        // A replay of the purged operation is still recognised as the same delivery.
        purged.Matches(expired.RouteId, expired.ShipmentId, DeliveryOutcome.Delivered, expired.ActorSubject, "Marie Dupont", null, PersistenceTests.Png(), null).Should().BeTrue();
        (await new DeliveryProofMaintenance(database, TestProofKeys.Settings(retentionDays: 30), new FixedClock(now)).RunAsync(default)).Purged.Should().Be(0);
    }

    [Fact]
    public async Task Receipt_reads_no_image_bytes_and_image_reads_load_one_column()
    {
        await using (var setup = CreateContext()) await setup.Database.MigrateAsync();
        var sql = new List<string>();
        await using var database = new DeliveryDbContext(new DbContextOptionsBuilder<DeliveryDbContext>().UseNpgsql(infrastructure.Postgres.GetConnectionString())
            .LogTo(message => { lock (sql) sql.Add(message); }, [Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted]).Options, TestProofKeys.Protector);
        var attempt = await AddAttemptAsync(database, DateTimeOffset.UtcNow);
        var store = new DeliveryStore(database, Polly.ResiliencePipeline.Empty);
        sql.Clear();

        var receipt = await store.FindAttemptReceiptByStopAsync(attempt.RouteId, attempt.ShipmentId, default);

        receipt.Should().BeEquivalentTo(new { attempt.OperationId, RecipientName = "Marie Dupont", HasSignature = true, HasPhoto = true, ProofsPurgedAtUtc = (DateTimeOffset?)null });
        // The image columns only appear in NULL tests: their bytes are never transferred.
        sql.Should().ContainSingle().Which.Should().NotMatchRegex(@"""(SignaturePng|Photo)""(?!\s+IS NOT NULL)");
        sql.Clear();
        (await store.FindProofImageAsync(attempt.RouteId, attempt.ShipmentId, ProofImageKind.Photo, default))!.Content.Should().Equal(PersistenceTests.Png());
        sql.Should().ContainSingle().Which.Should().NotContain("\"SignaturePng\"");
        (await store.FindProofImageAsync(Guid.NewGuid(), attempt.ShipmentId, ProofImageKind.Photo, default)).Should().BeNull();
    }

    [Fact]
    public async Task Maintenance_encrypts_proofs_written_before_encryption()
    {
        await using var database = CreateContext(); await database.Database.MigrateAsync();
        var attempt = await AddAttemptAsync(database, DateTimeOffset.UtcNow);
        // State left by the migration on rows written by the previous versions: UTF-8 name, raw images.
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE delivery_attempts SET "RecipientName" = convert_to('Jean Martin', 'UTF8'), "SignaturePng" = {PersistenceTests.Png()}, "Photo" = NULL
            WHERE "OperationId" = {attempt.OperationId}
            """);
        (await database.Attempts.AsNoTracking().SingleAsync(x => x.OperationId == attempt.OperationId)).RecipientName.Should().Be("Jean Martin");

        var result = await new DeliveryProofMaintenance(database, TestProofKeys.Settings(), TimeProvider.System).RunAsync(default);

        result.Encrypted.Should().BeGreaterThanOrEqualTo(1);
        var raw = await RawAsync(attempt.OperationId);
        DeliveryProofProtector.IsProtected(raw.Name).Should().BeTrue(); DeliveryProofProtector.IsProtected(raw.Signature).Should().BeTrue(); raw.Photo.Should().BeNull();
        var restored = await database.Attempts.AsNoTracking().SingleAsync(x => x.OperationId == attempt.OperationId);
        restored.RecipientName.Should().Be("Jean Martin"); restored.SignaturePng.Should().Equal(PersistenceTests.Png());
    }
}
