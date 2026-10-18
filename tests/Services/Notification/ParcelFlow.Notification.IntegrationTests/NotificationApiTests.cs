using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ParcelFlow.Notification.Application;
using ParcelFlow.Notification.Domain;
using ParcelFlow.Notification.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Notification.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class NotificationApiTests(InfrastructureFixture infrastructure)
{
    private const string Issuer = "https://issuer.test";
    private const string Audience = "notification-api";
    private static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("ParcelFlow-test-only-signing-key-2026-never-for-production"));

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("operator", HttpStatusCode.Forbidden)]
    [InlineData("customer", HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    public async Task Private_notifications_require_customer_or_admin(string? role, HttpStatusCode expected)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory, role);
        using var response = await client.GetAsync("/api/v1/notifications");
        response.StatusCode.Should().Be(expected);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        if (expected != HttpStatusCode.OK) await AssertProblemAsync(response, expected);
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
    public async Task Invalid_signed_token_is_rejected(string fault)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken("admin", fault));
        using var response = await client.GetAsync("/api/v1/notifications");
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Customer_only_reads_own_messages_and_cannot_override_subject_by_query()
    {
        await using var factory = await CreateFactoryAsync();
        var owner = "owner-" + Guid.NewGuid().ToString("N");
        var shipment = await SeedAsync(factory, owner);
        var otherShipment = await SeedAsync(factory, "other-" + Guid.NewGuid().ToString("N"));
        using var customer = Client(factory, "customer", owner);
        using var page = JsonDocument.Parse(await customer.GetStringAsync("/api/v1/notifications?customerSubject=forged&ownerSubject=forged"));
        page.RootElement.GetProperty("total").GetInt32().Should().Be(2);
        var items = page.RootElement.GetProperty("items").EnumerateArray().ToArray();
        items.Should().OnlyContain(x => x.GetProperty("customerSubject").GetString() == owner);
        items.Should().OnlyContain(x => x.GetProperty("shipmentId").GetGuid() == shipment);
        items.Select(x => x.GetProperty("channel").GetString()).Should().BeEquivalentTo(new[] { "Email", "Sms" });
        foreach (var item in items)
        {
            item.GetProperty("deliveryStatus").GetString().Should().Be("MockSent");
            item.GetProperty("destination").GetString().Should().Be(item.GetProperty("channel").GetString() == "Email" ? "client@example.test" : "+33611223344");
            item.GetProperty("body").GetString().Should().Contain(factory.Recipients.Get(shipment).TrackingNumber);
            using var response = await customer.GetAsync($"/api/v1/notifications/{item.GetProperty("id").GetGuid()}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
        }
        using var admin = Client(factory, "admin");
        using var otherPage = JsonDocument.Parse(await admin.GetStringAsync($"/api/v1/notifications?shipmentId={otherShipment}"));
        otherPage.RootElement.GetProperty("total").GetInt32().Should().Be(2);
        var otherId = otherPage.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();
        using var denied = await customer.GetAsync($"/api/v1/notifications/{otherId}");
        await AssertProblemAsync(denied, HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().NotContain("client@example.test").And.NotContain("+33611223344");
        using var filtered = JsonDocument.Parse(await customer.GetStringAsync($"/api/v1/notifications?shipmentId={otherShipment}"));
        filtered.RootElement.GetProperty("total").GetInt32().Should().Be(0);
        using var visible = await admin.GetAsync($"/api/v1/notifications/{otherId}");
        visible.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(NotificationKind.ShipmentCreated)]
    [InlineData(NotificationKind.TrackingStatus)]
    [InlineData(NotificationKind.InvoiceIssued)]
    [InlineData(NotificationKind.PaymentCompleted)]
    [InlineData(NotificationKind.PaymentFailed)]
    [InlineData(NotificationKind.CreditNoteIssued)]
    public async Task Each_event_has_two_readable_mock_channel_receipts(NotificationKind kind)
    {
        await using var factory = await CreateFactoryAsync();
        var owner = "kind-" + Guid.NewGuid().ToString("N");
        var shipment = await SeedAsync(factory, owner, kind);
        using var client = Client(factory, "customer", owner);
        using var json = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/notifications?shipmentId={shipment}"));
        json.RootElement.GetProperty("total").GetInt32().Should().Be(2);
        var receipts = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
        receipts.Select(x => x.GetProperty("channel").GetString()).Should().BeEquivalentTo(new[] { "Email", "Sms" });
        foreach (var receipt in receipts)
        {
            receipt.GetProperty("subject").GetString().Should().NotBeNullOrWhiteSpace();
            receipt.GetProperty("deliveryStatus").GetString().Should().Be("MockSent");
            receipt.GetProperty("sourceEventId").GetGuid().Should().NotBeEmpty();
            receipt.GetProperty("occurredAtUtc").GetDateTimeOffset().Offset.Should().Be(TimeSpan.Zero);
        }
    }

    [Fact]
    public async Task Created_tracking_event_is_not_a_second_creation_notice()
    {
        await using var factory = await CreateFactoryAsync();
        var owner = "empty-" + Guid.NewGuid().ToString("N");
        var shipment = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var recorded = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RecordNotification(
            Guid.NewGuid(), shipment, NotificationKind.TrackingStatus, DateTimeOffset.UtcNow, null,
            "PF" + shipment.ToString("N")[..24].ToUpperInvariant(), ParcelStatus.Created, null, null, null, null));
        recorded.Should().BeFalse();
        factory.Recipients.Calls.Should().Be(0);
        using var client = Client(factory, "customer", owner);
        using var json = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/notifications?shipmentId={shipment}"));
        json.RootElement.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?shipmentId=00000000-0000-0000-0000-000000000000")]
    [InlineData("?shipmentId=invalid")]
    public async Task Invalid_query_returns_uniform_problem_details(string query)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory, "admin");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/xml");
        using var response = await client.GetAsync("/api/v1/notifications" + query);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Pagination_is_stable_and_missing_receipt_is_not_found()
    {
        await using var factory = await CreateFactoryAsync();
        var owner = "pages-" + Guid.NewGuid().ToString("N");
        var shipment = await SeedAsync(factory, owner);
        using var client = Client(factory, "customer", owner);
        using var first = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/notifications?shipmentId={shipment}&pageSize=1"));
        using var second = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/notifications?shipmentId={shipment}&pageSize=1&page=2"));
        first.RootElement.GetProperty("total").GetInt32().Should().Be(2);
        first.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid().Should().NotBe(second.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());
        using var absent = await client.GetAsync($"/api/v1/notifications/{Guid.NewGuid()}");
        await AssertProblemAsync(absent, HttpStatusCode.NotFound);
        using var emptyId = await client.GetAsync("/api/v1/notifications/00000000-0000-0000-0000-000000000000");
        await AssertProblemAsync(emptyId, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Health_metrics_and_read_only_string_enum_openapi_are_available()
    {
        await using var factory = new JwtApiFactory(infrastructure, "Development");
        await factory.Services.InitializeNotificationDatabaseAsync();
        using var client = Client(factory, "admin");
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/metrics")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v2/notifications")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var post = await client.PostAsJsonAsync("/api/v1/notifications", new { destination = "injected@example.test" });
        post.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = json.RootElement.GetProperty("paths").EnumerateObject().ToArray();
        paths.Should().HaveCount(2);
        paths.Should().OnlyContain(x => x.Value.EnumerateObject().All(method => method.Name == "get"));
        var schema = json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("NotificationChannel");
        schema.GetProperty("type").GetString().Should().Be("string");
        schema.GetProperty("enum").EnumerateArray().Select(x => x.GetString()).Should().BeEquivalentTo(new[] { "Email", "Sms" });
    }

    private static async Task<Guid> SeedAsync(JwtApiFactory factory, string owner, NotificationKind kind = NotificationKind.ShipmentCreated)
    {
        var shipment = Guid.NewGuid();
        var trackingNumber = "PF" + shipment.ToString("N")[..24].ToUpperInvariant();
        factory.Recipients.Add(shipment, new ShipmentRecipient(owner, trackingNumber, "client@example.test", "+33611223344"));
        var command = kind switch
        {
            NotificationKind.ShipmentCreated => new RecordNotification(Guid.NewGuid(), shipment, kind, DateTimeOffset.UtcNow, owner, trackingNumber, null, null, null, null, null),
            NotificationKind.TrackingStatus => new RecordNotification(Guid.NewGuid(), shipment, kind, DateTimeOffset.UtcNow, null, trackingNumber, ParcelStatus.Delivered, null, null, null, null),
            NotificationKind.InvoiceIssued => new RecordNotification(Guid.NewGuid(), shipment, kind, DateTimeOffset.UtcNow, owner, null, null, shipment, null, 19.95m, "EUR"),
            NotificationKind.PaymentCompleted => new RecordNotification(Guid.NewGuid(), shipment, kind, DateTimeOffset.UtcNow, null, null, null, null, null, 19.95m, "EUR"),
            NotificationKind.PaymentFailed => new RecordNotification(Guid.NewGuid(), shipment, kind, DateTimeOffset.UtcNow, null, null, null, null, null, null, null),
            NotificationKind.CreditNoteIssued => new RecordNotification(Guid.NewGuid(), shipment, kind, DateTimeOffset.UtcNow, owner, null, null, shipment, Guid.NewGuid(), 4.50m, "EUR"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
        return shipment;
    }

    private sealed class TestRecipientResolver : IShipmentRecipientResolver
    {
        private readonly ConcurrentDictionary<Guid, ShipmentRecipient> _recipients = new();
        private int _calls;
        public int Calls => _calls;
        public void Add(Guid id, ShipmentRecipient recipient) => _recipients[id] = recipient;
        public ShipmentRecipient Get(Guid id) => _recipients[id];
        public Task<ShipmentRecipient> ResolveAsync(Guid shipmentId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(Get(shipmentId));
        }
    }
    private static HttpClient Client(JwtApiFactory factory, string? role, string subject = "customer-test")
    {
        var client = factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(role, subject: subject));
        return client;
    }
    private async Task<JwtApiFactory> CreateFactoryAsync()
    {
        var factory = new JwtApiFactory(infrastructure);
        await factory.Services.InitializeNotificationDatabaseAsync();
        return factory;
    }
    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.Should().Be(expected);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetInt32().Should().Be((int)expected);
        json.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
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

    private sealed class JwtApiFactory(InfrastructureFixture dependencies, string environment = "Testing") : WebApplicationFactory<Program>
    {
        public TestRecipientResolver Recipients { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:NotificationDatabase"] = dependencies.Postgres.GetConnectionString(),
                    ["ConnectionStrings:Redis"] = dependencies.Redis.GetConnectionString(),
                    ["RabbitMq:Host"] = dependencies.RabbitMq.Hostname,
                    ["RabbitMq:Port"] = dependencies.RabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["RabbitMq:VirtualHost"] = "/",
                    ["RabbitMq:Username"] = "rabbitmq",
                    ["RabbitMq:Password"] = "rabbitmq",
                    ["Database:Initialize"] = "false",
                    ["Database:SeedDemoData"] = "false"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IShipmentRecipientResolver>();
                services.AddSingleton<IShipmentRecipientResolver>(Recipients);
            });
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
