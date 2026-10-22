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
using ParcelFlow.Warehouse.Application;
using ParcelFlow.Warehouse.Domain;
using ParcelFlow.Warehouse.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Warehouse.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class WarehouseApiTests(InfrastructureFixture infrastructure)
{
    private const string Issuer = "https://issuer.test";
    private const string Audience = "warehouse-api";
    private static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("ParcelFlow-test-only-signing-key-2026-never-for-production"));

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("customer", HttpStatusCode.Forbidden)]
    [InlineData("operator", HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    public async Task Warehouse_reads_require_staff(string? role, HttpStatusCode expected)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory, role);
        using var response = await client.GetAsync("/api/v1/warehouses");
        response.StatusCode.Should().Be(expected);
        if (expected != HttpStatusCode.OK)
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        else response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Admin_creates_updates_and_reads_a_warehouse_with_immutable_identity()
    {
        await using var factory = await CreateFactoryAsync();
        using var admin = Client(factory, "admin");
        var code = "FR-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        using var created = await admin.PostAsJsonAsync("/api/v1/warehouses", WarehouseBody(code));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetGuid();
        created.Headers.Location!.ToString().Should().Be($"/api/v1/warehouses/{id}");
        json.RootElement.GetProperty("kind").GetString().Should().Be("SortingCenter");
        using var update = await admin.PutAsJsonAsync($"/api/v1/warehouses/{id}", new
        {
            name = "Centre de tri Lyon Sud", street = "18 avenue des Freres Lumiere", city = "Lyon",
            postalCode = "69008", countryCode = "fr", isActive = false
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        using var read = await admin.GetAsync(created.Headers.Location);
        using var detail = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        detail.RootElement.GetProperty("code").GetString().Should().Be(code);
        detail.RootElement.GetProperty("countryCode").GetString().Should().Be("FR");
        detail.RootElement.GetProperty("name").GetString().Should().Be("Centre de tri Lyon Sud");
        detail.RootElement.GetProperty("isActive").GetBoolean().Should().BeFalse();
        using var duplicate = await admin.PostAsJsonAsync("/api/v1/warehouses", WarehouseBody(code));
        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict);
        using var unknown = await admin.GetAsync($"/api/v1/warehouses/{Guid.NewGuid()}");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Operator_cannot_create_or_update_warehouses()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory, "operator");
        (await client.PostAsJsonAsync("/api/v1/warehouses", WarehouseBody("FR-TEST")))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync($"/api/v1/warehouses/{Guid.NewGuid()}", new
        {
            name = "Centre", street = "18 avenue", city = "Lyon", postalCode = "69008", countryCode = "FR", isActive = true
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("kind")]
    [InlineData("name")]
    [InlineData("null-character")]
    public async Task Invalid_warehouse_returns_validation_problem(string fault)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory, "admin");
        using var response = await client.PostAsJsonAsync("/api/v1/warehouses", new
        {
            code = fault == "code" ? "invalid code!" : "FR-" + Guid.NewGuid().ToString("N")[..12],
            name = fault switch { "name" => "", "null-character" => "Dépôt\0Lyon", _ => "Centre de tri Lyon" }, kind = fault == "kind" ? 999 : 1,
            street = "18 avenue", city = "Lyon", postalCode = "69008", countryCode = "FR"
        });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("errors");
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Invalid_pagination_is_rejected(int page, int pageSize)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory, "operator");
        using var response = await client.GetAsync($"/api/v1/warehouses?page={page}&pageSize={pageSize}");
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Scan_entry_exit_and_replay_update_stock_immediately()
    {
        await using var factory = await CreateFactoryAsync();
        var warehouseId = await SeedWarehouseAsync(factory);
        var shipmentId = Guid.NewGuid();
        var number = "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RegisterReadyShipment(shipmentId, number));
        using var client = Client(factory, "operator");
        var scanId = Guid.NewGuid();
        var entry = new { scanId, warehouseId, trackingNumber = number.ToLowerInvariant(), direction = "Entry", operatorSubject = "forged-subject" };
        using var first = await client.PostAsJsonAsync("/api/v1/warehouses/scans", entry);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var scope = factory.Services.CreateScope())
        {
            var scan = await scope.ServiceProvider.GetRequiredService<IWarehouseStore>().FindScanAsync(scanId, CancellationToken.None);
            scan!.OperatorSubject.Should().Be("operator-demo");
        }
        using var replay = await client.PostAsJsonAsync("/api/v1/warehouses/scans", entry);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());
        using var stock = await client.GetAsync($"/api/v1/warehouses/{warehouseId}/stock?page=1&pageSize=1");
        stock.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var json = JsonDocument.Parse(await stock.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("total").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("items")[0].GetProperty("trackingNumber").GetString().Should().Be(number);
        using var repeatedEntry = await client.PostAsJsonAsync("/api/v1/warehouses/scans", new
        { scanId = Guid.NewGuid(), warehouseId, trackingNumber = number, direction = "Entry" });
        await AssertProblemAsync(repeatedEntry, HttpStatusCode.Conflict);
        using var reusedId = await client.PostAsJsonAsync("/api/v1/warehouses/scans", new
        { scanId, warehouseId, trackingNumber = number, direction = "Exit" });
        await AssertProblemAsync(reusedId, HttpStatusCode.Conflict);
        using var exit = await client.PostAsJsonAsync("/api/v1/warehouses/scans", new
        { scanId = Guid.NewGuid(), warehouseId, trackingNumber = number, direction = "Exit" });
        exit.StatusCode.Should().Be(HttpStatusCode.OK);
        using var empty = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/warehouses/{warehouseId}/stock"));
        empty.RootElement.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Scan_unknown_and_unready_parcels_return_problem_details()
    {
        await using var factory = await CreateFactoryAsync();
        var warehouseId = await SeedWarehouseAsync(factory);
        using var client = Client(factory, "operator");
        using var unknown = await client.PostAsJsonAsync("/api/v1/warehouses/scans", new
        { scanId = Guid.NewGuid(), warehouseId, trackingNumber = "PF" + Guid.NewGuid().ToString("N")[..24], direction = "Entry" });
        await AssertProblemAsync(unknown, HttpStatusCode.Conflict);
        using var invalid = await client.PostAsJsonAsync("/api/v1/warehouses/scans", new
        { scanId = Guid.Empty, warehouseId, trackingNumber = "invalid", direction = 999 });
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest);
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
        using var response = await client.GetAsync("/api/v1/warehouses");
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_metrics_are_public_and_unsupported_versions_are_rejected()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/metrics")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v2/warehouses")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OpenApi_describes_enum_strings_consistently_with_json_responses()
    {
        await using var factory = new JwtApiFactory(infrastructure, "Development");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var (name, values) in new[]
        {
            ("WarehouseKind", new[] { "Warehouse", "SortingCenter" }),
            ("ScanDirection", new[] { "Entry", "Exit" })
        })
        {
            var schema = schemas.GetProperty(name);
            schema.GetProperty("type").GetString().Should().Be("string");
            schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString()).Should().Equal(values);
        }
        schemas.GetProperty("CreateWarehouseBody").GetProperty("required").EnumerateArray()
            .Select(value => value.GetString()).Should().Contain("kind");
        schemas.GetProperty("UpdateWarehouseBody").GetProperty("required").EnumerateArray()
            .Select(value => value.GetString()).Should().Contain("isActive");
        schemas.GetProperty("ScanParcelBody").GetProperty("required").EnumerateArray()
            .Select(value => value.GetString()).Should().Contain("direction");
        json.RootElement.GetProperty("paths").TryGetProperty("/api/v1/warehouses/scans", out _).Should().BeTrue();
    }
    [Fact]
    public async Task Missing_scan_direction_is_rejected_without_moving_a_ready_parcel()
    {
        await using var factory = await CreateFactoryAsync();
        var warehouseId = await SeedWarehouseAsync(factory);
        var number = "PF" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RegisterReadyShipment(Guid.NewGuid(), number));
        using var client = Client(factory, "operator");
        using var response = await client.PostAsJsonAsync("/api/v1/warehouses/scans", new
        { scanId = Guid.NewGuid(), warehouseId, trackingNumber = number });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        using var stock = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/warehouses/{warehouseId}/stock"));
        stock.RootElement.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Missing_warehouse_kind_is_rejected_instead_of_defaulting_to_warehouse()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory, "admin");
        using var response = await client.PostAsJsonAsync("/api/v1/warehouses", new
        {
            code = "FR-" + Guid.NewGuid().ToString("N")[..12], name = "Centre de tri Lyon",
            street = "18 avenue", city = "Lyon", postalCode = "69008", countryCode = "FR"
        });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }
    [Fact]
    public async Task Missing_is_active_is_rejected_without_deactivating_a_warehouse()
    {
        await using var factory = await CreateFactoryAsync();
        var warehouseId = await SeedWarehouseAsync(factory);
        using var client = Client(factory, "admin");
        using var response = await client.PutAsJsonAsync($"/api/v1/warehouses/{warehouseId}", new
        {
            name = "Centre de tri Lyon", street = "18 avenue", city = "Lyon",
            postalCode = "69008", countryCode = "FR"
        });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        using var read = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/warehouses/{warehouseId}"));
        read.RootElement.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }
    private static object WarehouseBody(string code) => new
    { code, name = "Centre de tri Lyon", kind = "SortingCenter", street = "18 avenue des Freres Lumiere", city = "Lyon", postalCode = "69008", countryCode = "FR" };

    private static HttpClient Client(JwtApiFactory factory, string? role)
    {
        var client = factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(role, subject: "operator-demo"));
        return client;
    }

    private static async Task<Guid> SeedWarehouseAsync(JwtApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dto = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CreateWarehouse(Guid.NewGuid(),
            "FR-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Centre de tri Lyon", WarehouseKind.SortingCenter,
            "18 avenue des Freres Lumiere", "Lyon", "69008", "FR"));
        return dto.Id;
    }

    private async Task<JwtApiFactory> CreateFactoryAsync()
    {
        var factory = new JwtApiFactory(infrastructure);
        await factory.Services.InitializeWarehouseDatabaseAsync();
        return factory;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.Should().Be(expected);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
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
                    ["ConnectionStrings:WarehouseDatabase"] = dependencies.Postgres.GetConnectionString(),
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
