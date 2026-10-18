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
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ParcelFlow.Delivery.Application;
using ParcelFlow.Delivery.Domain;
using ParcelFlow.Delivery.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Delivery.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class DeliveryApiTests(InfrastructureFixture infrastructure)
{
    private const string Issuer = "https://issuer.test";
    private const string Audience = "delivery-api";
    private static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("ParcelFlow-test-only-signing-key-2026-never-for-production"));

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGNgAAIAAAUAAXpeqz8AAAAASUVORK5CYII=");
    private static readonly byte[] Jpeg = Convert.FromBase64String("/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD3+iiigD//2Q==");

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("customer", HttpStatusCode.Forbidden)]
    [InlineData("operator", HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    public async Task Routes_are_restricted_to_operations(string? role, HttpStatusCode expected)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory, role);
        using var response = await client.GetAsync("/api/v1/delivery/routes");
        response.StatusCode.Should().Be(expected);
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
        using var response = await client.GetAsync("/api/v1/delivery/routes");
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Planner_assigns_but_only_assigned_driver_or_admin_can_start_and_deliver()
    {
        await using var factory = await CreateFactoryAsync();
        var (routeId, shipmentId, driver) = await PrepareRouteAsync(factory);
        using var planner = Client(factory, "operator", "planner");
        using var forbiddenStart = await planner.PostAsync($"/api/v1/delivery/routes/{routeId}/start", null);
        await AssertProblemAsync(forbiddenStart, HttpStatusCode.Forbidden);
        using var courier = Client(factory, "operator", driver);
        using var start = await courier.PostAsync($"/api/v1/delivery/routes/{routeId}/start", null);
        start.StatusCode.Should().Be(HttpStatusCode.OK);
        var operationId = Guid.NewGuid();
        var body = new { operationId, recipientName = "Camille Martin", signaturePng = Png, actorSubject = "forged" };
        var path = $"/api/v1/delivery/routes/{routeId}/stops/{shipmentId}/delivery";
        using var denied = await planner.PostAsJsonAsync(path, body);
        await AssertProblemAsync(denied, HttpStatusCode.Forbidden);
        using var delivery = await courier.PostAsJsonAsync(path, body);
        delivery.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await delivery.Content.ReadAsStringAsync();
        using var attempt = JsonDocument.Parse(content);
        attempt.RootElement.GetProperty("actorSubject").GetString().Should().Be(driver);
        attempt.RootElement.GetProperty("hasSignature").GetBoolean().Should().BeTrue();
        attempt.RootElement.TryGetProperty("signaturePng", out _).Should().BeFalse();
        using var replay = await courier.PostAsJsonAsync(path, body);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).Should().Be(content);
        using var conflict = await courier.PostAsJsonAsync(path, new { operationId, recipientName = "Other", signaturePng = Png });
        await AssertProblemAsync(conflict, HttpStatusCode.Conflict);
        using var proofDenied = await planner.GetAsync($"/api/v1/delivery/routes/{routeId}/stops/{shipmentId}/proof");
        await AssertProblemAsync(proofDenied, HttpStatusCode.Forbidden);
        using var admin = Client(factory, "admin", "auditor");
        using var adminProof = await admin.GetAsync($"/api/v1/delivery/routes/{routeId}/stops/{shipmentId}/proof");
        adminProof.StatusCode.Should().Be(HttpStatusCode.OK);
        using var proof = await courier.GetAsync($"/api/v1/delivery/routes/{routeId}/stops/{shipmentId}/proof/signature");
        proof.StatusCode.Should().Be(HttpStatusCode.OK);
        (await proof.Content.ReadAsByteArrayAsync()).Should().Equal(Png);
        proof.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        proof.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        proof.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        proof.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var route = JsonDocument.Parse(await courier.GetStringAsync($"/api/v1/delivery/routes/{routeId}"));
        route.RootElement.GetProperty("status").GetString().Should().Be("Completed");
        using var unknownKind = await courier.GetAsync($"/api/v1/delivery/routes/{routeId}/stops/{shipmentId}/proof/unknown");
        await AssertProblemAsync(unknownKind, HttpStatusCode.BadRequest);
        using var absentPhoto = await courier.GetAsync($"/api/v1/delivery/routes/{routeId}/stops/{shipmentId}/proof/photo");
        await AssertProblemAsync(absentPhoto, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("invalid-signature")]
    [InlineData("signature-size")]
    [InlineData("photo-size")]
    [InlineData("recipient")]
    [InlineData("operation")]
    public async Task Invalid_proof_is_rejected_without_completing_stop(string fault)
    {
        await using var factory = await CreateFactoryAsync();
        var (routeId, shipmentId, driver) = await PrepareRouteAsync(factory);
        using var client = Client(factory, "operator", driver);
        (await client.PostAsync($"/api/v1/delivery/routes/{routeId}/start", null)).EnsureSuccessStatusCode();
        using var response = await client.PostAsJsonAsync($"/api/v1/delivery/routes/{routeId}/stops/{shipmentId}/delivery", new
        {
            operationId = fault == "operation" ? Guid.Empty : Guid.NewGuid(),
            recipientName = fault == "recipient" ? "Name\0bad" : "Camille Martin",
            signaturePng = fault switch { "none" => null, "invalid-signature" => new byte[] { 1, 2, 3 }, "signature-size" => new byte[2 * 1024 * 1024 + 1], _ => Png },
            photo = fault == "photo-size" ? new byte[5 * 1024 * 1024 + 1] : null
        });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        using var route = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/delivery/routes/{routeId}"));
        route.RootElement.GetProperty("stops")[0].GetProperty("status").GetString().Should().Be("Pending");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_stop_releases_parcel_for_replanning_and_photo_only_proof_is_accepted(bool jpeg)
    {
        await using var factory = await CreateFactoryAsync();
        var (routeId, shipmentId, driver) = await PrepareRouteAsync(factory);
        using var client = Client(factory, "operator", driver);
        (await client.PostAsync($"/api/v1/delivery/routes/{routeId}/start", null)).EnsureSuccessStatusCode();
        var body = new { operationId = Guid.NewGuid(), reason = "RecipientAbsent" };
        var path = $"/api/v1/delivery/routes/{routeId}/stops/{shipmentId}/failure";
        using var failure = await client.PostAsJsonAsync(path, body);
        failure.StatusCode.Should().Be(HttpStatusCode.OK);
        using var replay = await client.PostAsJsonAsync(path, body);
        (await replay.Content.ReadAsStringAsync()).Should().Be(await failure.Content.ReadAsStringAsync());
        using var available = JsonDocument.Parse(await client.GetStringAsync("/api/v1/delivery/parcels/available?pageSize=100"));
        available.RootElement.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("shipmentId").GetGuid() == shipmentId).Should().BeTrue();
        using var created = await client.PostAsJsonAsync("/api/v1/delivery/routes", new { name = "Seconde tentative", serviceDate = DateOnly.FromDateTime(DateTime.UtcNow) });
        var route = await created.Content.ReadFromJsonAsync<RouteDto>(JsonOptions);
        (await client.PutAsJsonAsync($"/api/v1/delivery/routes/{route!.Id}/driver", new { driverSubject = driver })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/v1/delivery/routes/{route.Id}/stops", new { shipmentId })).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/v1/delivery/routes/{route.Id}/start", null)).EnsureSuccessStatusCode();
        var photo = jpeg ? Jpeg : Png;
        using var delivered = await client.PostAsJsonAsync($"/api/v1/delivery/routes/{route.Id}/stops/{shipmentId}/delivery", new { operationId = Guid.NewGuid(), recipientName = "Camille Martin", photo });
        delivered.StatusCode.Should().Be(HttpStatusCode.OK);
        using var image = await client.GetAsync($"/api/v1/delivery/routes/{route.Id}/stops/{shipmentId}/proof/photo");
        image.StatusCode.Should().Be(HttpStatusCode.OK);
        image.Content.Headers.ContentType!.MediaType.Should().Be(jpeg ? "image/jpeg" : "image/png");
        (await image.Content.ReadAsByteArrayAsync()).Should().Equal(photo);
    }

    [Theory]
    [InlineData("serviceDate")]
    [InlineData("isActive")]
    [InlineData("shipmentId")]
    [InlineData("operationId")]
    [InlineData("reason")]
    public async Task Missing_decision_fields_return_bad_request(string field)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory, "admin");
        var route = Guid.NewGuid();
        var (path, body, update) = field switch
        {
            "serviceDate" => ("/api/v1/delivery/routes", new { name = "Route" } as object, false),
            "isActive" => ("/api/v1/delivery/drivers/driver-test", new { displayName = "Driver" } as object, true),
            "shipmentId" => ($"/api/v1/delivery/routes/{route}/stops", new { } as object, false),
            "operationId" => ($"/api/v1/delivery/routes/{route}/stops/{Guid.NewGuid()}/delivery", new { recipientName = "Test", signaturePng = Png } as object, false),
            _ => ($"/api/v1/delivery/routes/{route}/stops/{Guid.NewGuid()}/failure", new { operationId = Guid.NewGuid() } as object, false)
        };
        using var response = update ? await client.PutAsJsonAsync(path, body) : await client.PostAsJsonAsync(path, body);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Driver_management_is_admin_only_and_planning_requires_ready_picked_up_parcels()
    {
        await using var factory = await CreateFactoryAsync();
        using var planner = Client(factory, "operator", "planner");
        using var denied = await planner.PostAsJsonAsync("/api/v1/delivery/drivers", new { subject = "test", displayName = "Test" });
        await AssertProblemAsync(denied, HttpStatusCode.Forbidden);
        using var updateDenied = await planner.PutAsJsonAsync("/api/v1/delivery/drivers/test", new { displayName = "Test", isActive = false });
        await AssertProblemAsync(updateDenied, HttpStatusCode.Forbidden);
        using var created = await planner.PostAsJsonAsync("/api/v1/delivery/routes", new { name = "Planification", serviceDate = DateOnly.FromDateTime(DateTime.UtcNow) });
        var route = await created.Content.ReadFromJsonAsync<RouteDto>(JsonOptions);
        using var unready = await planner.PostAsJsonAsync($"/api/v1/delivery/routes/{route!.Id}/stops", new { shipmentId = Guid.NewGuid() });
        await AssertProblemAsync(unready, HttpStatusCode.NotFound);
        var unpickedShipment = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RegisterReadyShipment(unpickedShipment,
                "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant()));
        using var unpicked = await planner.PostAsJsonAsync($"/api/v1/delivery/routes/{route.Id}/stops", new { shipmentId = unpickedShipment });
        await AssertProblemAsync(unpicked, HttpStatusCode.Conflict);
        using var badPage = await planner.GetAsync("/api/v1/delivery/routes?page=0");
        await AssertProblemAsync(badPage, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Health_metrics_and_openapi_are_available_with_string_enums_and_required_fields()
    {
        await using var factory = new JwtApiFactory(infrastructure, "Development");
        using var client = factory.CreateClient();
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/metrics")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v2/delivery/routes")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        schemas.GetProperty("DeliveryFailureReason").GetProperty("type").GetString().Should().Be("string");
        foreach (var (name, field) in new[] { ("CreateRouteBody", "serviceDate"), ("UpdateDriverBody", "isActive"), ("AddStopBody", "shipmentId"), ("ConfirmDeliveryBody", "operationId"), ("FailDeliveryBody", "reason") })
            schemas.GetProperty(name).GetProperty("required").EnumerateArray().Select(x => x.GetString()).Should().Contain(field);
    }

    [Fact]
    public async Task Oversized_json_is_rejected_before_deserialization()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory, "admin");
        using var content = new StringContent(new string(' ', 12 * 1024 * 1024 + 1), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync($"/api/v1/delivery/routes/{Guid.NewGuid()}/stops/{Guid.NewGuid()}/delivery", content);
        await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge);
    }

    private static async Task<(Guid RouteId, Guid ShipmentId, string Driver)> PrepareRouteAsync(JwtApiFactory factory)
    {
        var shipmentId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new RegisterReadyShipment(shipmentId, "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant()));
            await sender.Send(new RegisterParcelPickup(shipmentId));
        }
        var driver = "driver-" + Guid.NewGuid().ToString("N");
        using var admin = Client(factory, "admin");
        (await admin.PostAsJsonAsync("/api/v1/delivery/drivers", new { subject = driver, displayName = "Alex Martin" })).EnsureSuccessStatusCode();
        using var response = await admin.PostAsJsonAsync("/api/v1/delivery/routes", new { name = "Tournée Lyon", serviceDate = DateOnly.FromDateTime(DateTime.UtcNow) });
        var route = await response.Content.ReadFromJsonAsync<RouteDto>(JsonOptions);
        (await admin.PutAsJsonAsync($"/api/v1/delivery/routes/{route!.Id}/driver", new { driverSubject = driver })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/v1/delivery/routes/{route.Id}/stops", new { shipmentId })).EnsureSuccessStatusCode();
        return (route.Id, shipmentId, driver);
    }


    private static HttpClient Client(JwtApiFactory factory, string? role, string subject = "driver-test")
    {
        var client = factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(role, subject: subject));
        return client;
    }
    private async Task<JwtApiFactory> CreateFactoryAsync()
    {
        var factory = new JwtApiFactory(infrastructure);
        await factory.Services.InitializeDeliveryDatabaseAsync();
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
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DeliveryDatabase"] = dependencies.Postgres.GetConnectionString(),
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
