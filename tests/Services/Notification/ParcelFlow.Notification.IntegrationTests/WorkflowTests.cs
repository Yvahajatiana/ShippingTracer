extern alias ShipmentApi;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.Notification.Application;
using ParcelFlow.Notification.Domain;
using ParcelFlow.Notification.Infrastructure;
using ParcelFlow.Notification.Infrastructure.Persistence;
using ParcelFlow.Shipment.Domain;
using ParcelFlow.Shipment.Infrastructure;
using ParcelFlow.Shipment.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Notification.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class WorkflowTests(InfrastructureFixture dependencies)
{
    private const string Issuer = "https://workflow-keycloak.test/realms/parcelflow";
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("ParcelFlow-notification-workflow-signing-key-test-only-2026"));

    [Fact]
    public async Task Out_of_order_facts_resolve_sender_through_real_protected_shipment_api_and_record_each_channel_once()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await using var source = new ShipmentFactory(dependencies, Connection("notification_source_" + suffix));
        await source.Services.InitializeShipmentDatabaseAsync();
        var parcel = new ParcelShipment("notification-workflow-customer", "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant(),
            new ShippingAddress("Emma Martin", "emma@example.test", "+33600000001", "12 rue des Peupliers", null, "69003", "Lyon", "FR"),
            new ShippingAddress("Lucas Bernard", "lucas@example.test", "+33600000002", "28 rue des Tilleuls", null, "75012", "Paris", "FR"),
            new ParcelSpecification(2, 20, 15, 10), 7.30m, DeliveryZone.Domestic);
        await using (var scope = source.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ShipmentDbContext>();
            database.Shipments.Add(parcel);
            await database.SaveChangesAsync();
        }
        using var unauthenticated = source.CreateClient();
        (await unauthenticated.GetAsync($"/api/v1/shipments/{parcel.Id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var routing = new WorkflowHttpClients(source);
        using var target = Host.CreateDefaultBuilder().UseEnvironment(Environments.Development)
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(Settings("NotificationDatabase", Connection("notification_target_" + suffix))))
            .ConfigureServices((context, services) =>
            {
                services.AddNotificationApplication().AddNotificationInfrastructure(context.Configuration);
                services.AddScoped<INotificationActor, WorkflowActor>();
                services.Replace(ServiceDescriptor.Singleton<IHttpClientFactory>(routing));
            }).Build();
        await target.Services.InitializeNotificationDatabaseAsync();
        await target.StartAsync();
        try
        {
            // Sender data already exists; payment may reach this service before ShipmentCreated.
            var bus = source.Services.GetRequiredService<IBus>();
            var paid = new PaymentCompleted(Guid.NewGuid(), parcel.Id, DateTimeOffset.UtcNow, parcel.Id, parcel.Price, "EUR");
            await bus.Publish(paid);
            await WaitForCount(target, parcel.Id, 2);
            var created = new ShipmentCreated(Guid.NewGuid(), parcel.Id, DateTimeOffset.UtcNow, parcel.Id, parcel.TrackingNumber, parcel.OwnerSubject, parcel.Price, "EUR");
            await bus.Publish(created);
            await WaitForCount(target, parcel.Id, 4);
            await bus.Publish(paid);
            await bus.Publish(created);
            await using var scope = target.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            var messages = await database.Messages.Where(x => x.ShipmentId == parcel.Id).ToListAsync();
            messages.Should().HaveCount(4);
            messages.Should().OnlyContain(x => x.CustomerSubject == parcel.OwnerSubject && x.TrackingNumber == parcel.TrackingNumber && x.DeliveryStatus == "MockSent");
            messages.Where(x => x.Channel == NotificationChannel.Email).Should().HaveCount(2).And.OnlyContain(x => x.Destination == "emma@example.test");
            messages.Where(x => x.Channel == NotificationChannel.Sms).Should().HaveCount(2).And.OnlyContain(x => x.Destination == "+33600000001");
            routing.TokenRequests.Should().Be(1);
            routing.ShipmentRequests.Should().Be(1, "the immutable sender snapshot is cached in real Redis");
        }
        finally { await target.StopAsync(); }
    }

    private string Connection(string database) => new NpgsqlConnectionStringBuilder(dependencies.Postgres.GetConnectionString()) { Database = database }.ConnectionString;
    private Dictionary<string, string?> Settings(string key, string connection) => Settings(dependencies, key, connection);
    private static Dictionary<string, string?> Settings(InfrastructureFixture fixture, string key, string connection) => new()
    {
        [$"ConnectionStrings:{key}"] = connection,
        ["ConnectionStrings:Redis"] = fixture.Redis.GetConnectionString(),
        ["RabbitMq:Host"] = fixture.RabbitMq.Hostname,
        ["RabbitMq:Port"] = fixture.RabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["RabbitMq:Username"] = "rabbitmq", ["RabbitMq:Password"] = "rabbitmq",
        ["Database:Initialize"] = "false", ["Database:SeedDemoData"] = "false",
        ["Keycloak:Authority"] = Issuer, ["ShipmentService:BaseUrl"] = "http://shipment.test/",
        ["ServiceAuthentication:ClientId"] = "notification-service", ["ServiceAuthentication:ClientSecret"] = "workflow-secret-test-only"
    };
    private static async Task WaitForCount(IHost target, Guid shipmentId, int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        while (true)
        {
            await using var scope = target.Services.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Messages.CountAsync(x => x.ShipmentId == shipmentId, deadline.Token) == count) return;
            await Task.Delay(100, deadline.Token);
        }
    }
    private sealed class WorkflowActor : INotificationActor
    {
        public string Subject => "notification-workflow-customer";
        public bool IsAdmin => false;
    }
    private sealed class ShipmentFactory(InfrastructureFixture fixture, string connection) : WebApplicationFactory<ShipmentApi::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(Settings(fixture, "ShipmentDatabase", connection)));
            builder.ConfigureServices(services => services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
                metadata.SigningKeys.Add(Key);
                options.Authority = null;
                options.MetadataAddress = string.Empty;
                options.Configuration = metadata;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = Issuer, ValidAudience = "shipment-api", IssuerSigningKey = Key,
                    ValidateIssuer = true, ValidateAudience = true, ValidateIssuerSigningKey = true, ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero, RoleClaimType = ClaimTypes.Role
                };
            }));
        }
    }
    private sealed class WorkflowHttpClients(ShipmentFactory source) : IHttpClientFactory
    {
        public int TokenRequests;
        public int ShipmentRequests;
        public HttpClient CreateClient(string name) => new(new Router(this, source.Server.CreateHandler()))
        {
            BaseAddress = new Uri("http://shipment.test/"), Timeout = TimeSpan.FromSeconds(5)
        };
        private sealed class Router(WorkflowHttpClients owner, HttpMessageHandler shipment) : HttpMessageHandler
        {
            private readonly HttpMessageInvoker _source = new(shipment);
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref owner.TokenRequests);
                    var form = await request.Content!.ReadAsStringAsync(cancellationToken);
                    form.Should().Contain("grant_type=client_credentials").And.Contain("client_id=notification-service").And.Contain("client_secret=workflow-secret-test-only");
                    var now = DateTime.UtcNow;
                    var jwt = new JwtSecurityToken(Issuer, "shipment-api",
                        [new Claim("sub", "service-account-notification-service"), new Claim("realm_access", "{\"roles\":[\"operator\"]}", JsonClaimValueTypes.Json)],
                        now.AddMinutes(-1), now.AddMinutes(5), new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(new { access_token = new JwtSecurityTokenHandler().WriteToken(jwt), expires_in = 300, token_type = "Bearer" })
                    };
                }
                Interlocked.Increment(ref owner.ShipmentRequests);
                request.Headers.Authorization!.Scheme.Should().Be("Bearer");
                return await _source.SendAsync(request, cancellationToken);
            }
            protected override void Dispose(bool disposing) { if (disposing) _source.Dispose(); base.Dispose(disposing); }
        }
    }
}
