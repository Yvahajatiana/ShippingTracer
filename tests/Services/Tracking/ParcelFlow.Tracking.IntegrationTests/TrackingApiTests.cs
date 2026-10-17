using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using ParcelFlow.Tracking.Infrastructure.Persistence;
using ParcelFlow.Tracking.Application;
using ParcelFlow.Tracking.Domain;
using Xunit;

namespace ParcelFlow.Tracking.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class TrackingApiTests(InfrastructureFixture infrastructure)
{
    private const string Issuer = "https://issuer.test";
    private const string Audience = "tracking-api";
    private static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("ParcelFlow-test-only-signing-key-2026-never-for-production"));

    [Fact]
    public async Task Anonymous_tracking_is_canonical_chronological_and_contains_only_public_facts()
    {
        await using var factory = await CreateFactoryAsync();
        var (shipmentId, trackingNumber) = await SeedTrackingAsync(factory);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/v1/tracking/{trackingNumber.ToLowerInvariant()}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        root.GetProperty("trackingNumber").GetString().Should().Be(trackingNumber);
        root.GetProperty("currentStatus").GetString().Should().Be("InTransit");
        root.GetProperty("currentStatusLabel").GetString().Should().Be("En transit");
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "trackingNumber", "currentStatus", "currentStatusLabel", "updatedAtUtc", "history");
        var history = root.GetProperty("history").EnumerateArray().ToArray();
        history.Select(x => x.GetProperty("statusLabel").GetString()).Should().Equal("Créé", "Pris en charge", "En transit");
        history.Select(x => x.GetProperty("occurredAtUtc").GetDateTimeOffset()).Should().BeInAscendingOrder();
        foreach (var entry in history)
            entry.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("status", "statusLabel", "occurredAtUtc", "facilityCode");
        root.GetRawText().Should().NotContain(shipmentId.ToString());
        using var cached = await client.GetAsync($"/api/v1/tracking/{trackingNumber}");
        (await cached.Content.ReadAsStringAsync()).Should().Be(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("PF00000000000000000000000G")]
    [InlineData("PF000000000000000000000000EXTRA")]
    public async Task Invalid_tracking_number_returns_validation_problem(string trackingNumber)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/v1/tracking/{trackingNumber}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("traceId");
    }

    [Fact]
    public async Task Unknown_number_is_not_cached_and_corrupt_cache_falls_back_to_database()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        var number = "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
        using var missing = await client.GetAsync($"/api/v1/tracking/{number}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var cache = factory.Services.GetRequiredService<IDistributedCache>();
        (await cache.GetAsync($"tracking:public:v1:{number}")).Should().BeNull();
        await SeedTrackingAsync(factory, number);
        await cache.SetStringAsync($"tracking:public:v1:{number}", "invalid-json");
        (await client.GetAsync($"/api/v1/tracking/{number}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cache_failure_or_timeout_does_not_prevent_tracking(bool timeout)
    {
        await using var factory = new JwtApiFactory(infrastructure, new UnavailableCache(timeout));
        await factory.Services.InitializeTrackingDatabaseAsync();
        var (_, number) = await SeedTrackingAsync(factory);
        using var client = factory.CreateClient();
        // Both cache reads and writes are unavailable; the real database still serves the request.
        using var response = await client.GetAsync($"/api/v1/tracking/{number}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("customer", HttpStatusCode.Forbidden)]
    [InlineData("operator", HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    public async Task Internal_tracking_requires_staff_and_preserves_pending_events(string? role, HttpStatusCode expected)
    {
        await using var factory = await CreateFactoryAsync();
        var id = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RecordTrackingEvent(
                Guid.NewGuid(), id, null, ParcelStatus.InTransit, DateTimeOffset.UtcNow, "FR-LYO-01"));
        using var client = factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(role));
        using var response = await client.GetAsync($"/api/v1/tracking/shipments/{id}");
        response.StatusCode.Should().Be(expected);
        if (expected != HttpStatusCode.OK) return;
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("shipmentId").GetGuid().Should().Be(id);
        json.RootElement.GetProperty("trackingNumber").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.GetProperty("history")[0].GetProperty("eventId").GetGuid().Should().NotBeEmpty();
        (await client.GetAsync($"/api/v1/tracking/shipments/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("missing-subject")]
    [InlineData("realm-array")]
    [InlineData("realm-null")]
    [InlineData("roles-string")]
    [InlineData("roles-number")]
    public async Task Invalid_signed_token_cannot_access_internal_tracking(string fault)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken("admin", fault));
        using var response = await client.GetAsync($"/api/v1/tracking/shipments/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Health_metrics_are_public_and_unsupported_versions_are_rejected()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/metrics")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v2/tracking/PF000000000000000000000000")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<(Guid ShipmentId, string TrackingNumber)> SeedTrackingAsync(JwtApiFactory factory, string? number = null)
    {
        var shipmentId = Guid.NewGuid();
        number ??= "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
        using var scope = factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var now = DateTimeOffset.UtcNow;
        await sender.Send(new RecordTrackingEvent(Guid.NewGuid(), shipmentId, null, ParcelStatus.InTransit, now, "FR-LYO-01"));
        await sender.Send(new RecordTrackingEvent(Guid.NewGuid(), shipmentId, number, ParcelStatus.Created, now.AddHours(-2), null));
        await sender.Send(new RecordTrackingEvent(Guid.NewGuid(), shipmentId, null, ParcelStatus.PickedUp, now.AddHours(-1), "FR-PAR-01"));
        return (shipmentId, number);
    }

    private async Task<JwtApiFactory> CreateFactoryAsync()
    {
        var factory = new JwtApiFactory(infrastructure);
        await factory.Services.InitializeTrackingDatabaseAsync();
        return factory;
    }

    private static string CreateToken(string role, string? fault = null, string? subject = null)
    {
        var now = DateTime.UtcNow;
        var realmAccess = fault switch
        {
            "realm-array" => "[]",
            "realm-null" => "null",
            "roles-string" => "{\"roles\":\"admin\"}",
            "roles-number" => "{\"roles\":[123]}",
            _ => JsonSerializer.Serialize(new { roles = new[] { role } })
        };
        List<Claim> claims =
        [
            new("realm_access", realmAccess, JsonClaimValueTypes.Json)
        ];
        if (fault != "missing-subject") claims.Add(new Claim("sub", subject ?? Guid.NewGuid().ToString()));
        var key = fault == "signature"
            ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes("ParcelFlow-test-only-incorrect-signing-key-never-for-production"))
            : SigningKey;
        var token = new JwtSecurityToken(
            issuer: fault == "issuer" ? "https://untrusted.test" : Issuer,
            audience: fault == "audience" ? "unrelated-api" : Audience,
            claims: claims,
            notBefore: now.AddMinutes(-30),
            expires: fault == "expired" ? now.AddMinutes(-10) : now.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class UnavailableCache(bool timeout) : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("Cache unavailable");
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new InvalidOperationException("Cache unavailable");
        public void Refresh(string key) => throw new InvalidOperationException("Cache unavailable");
        public void Remove(string key) => throw new InvalidOperationException("Cache unavailable");
        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            if (timeout) await Task.Delay(TimeSpan.FromSeconds(10), token);
            throw new InvalidOperationException("Cache unavailable");
        }
        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            if (timeout) await Task.Delay(TimeSpan.FromSeconds(10), token);
            throw new InvalidOperationException("Cache unavailable");
        }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.FromException(new InvalidOperationException("Cache unavailable"));
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.FromException(new InvalidOperationException("Cache unavailable"));
    }

    private sealed class JwtApiFactory(InfrastructureFixture dependencies, IDistributedCache? cache = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            if (cache is not null) builder.ConfigureServices(services => services.AddSingleton(cache));
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:TrackingDatabase"] = dependencies.Postgres.GetConnectionString(),
                    ["ConnectionStrings:Redis"] = dependencies.Redis.GetConnectionString(),
                    ["RabbitMq:Host"] = dependencies.RabbitMq.Hostname,
                    ["RabbitMq:Port"] = dependencies.RabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["RabbitMq:VirtualHost"] = "/",
                    ["RabbitMq:Username"] = "rabbitmq",
                    ["RabbitMq:Password"] = "rabbitmq",
                    ["Database:Initialize"] = "false",
                    ["Database:SeedDemoData"] = "false"
                }));
            builder.ConfigureServices(services => services.Configure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
                    metadata.SigningKeys.Add(SigningKey);
                    options.Authority = null;
                    options.MetadataAddress = string.Empty;
                    options.Configuration = metadata;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                    options.TokenValidationParameters.ValidIssuer = Issuer;
                    options.TokenValidationParameters.ValidAudience = Audience;
                    options.TokenValidationParameters.IssuerSigningKey = SigningKey;
                    options.TokenValidationParameters.ValidateIssuer = true;
                    options.TokenValidationParameters.ValidateAudience = true;
                    options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                    options.TokenValidationParameters.ValidateLifetime = true;
                    options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
                }));
        }
    }
}
