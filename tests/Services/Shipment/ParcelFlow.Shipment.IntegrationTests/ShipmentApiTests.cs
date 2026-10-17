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
using ParcelFlow.Shipment.Infrastructure.Persistence;
using ParcelFlow.Shipment.Application;
using Xunit;

namespace ParcelFlow.Shipment.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class ShipmentApiTests(InfrastructureFixture infrastructure)
{
    private const string Issuer = "https://issuer.test";
    private const string Audience = "shipment-api";
    private static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("ParcelFlow-test-only-signing-key-2026-never-for-production"));

    [Fact]
    public async Task Create_recomputes_quote_and_uses_authenticated_owner()
    {
        await using var factory = await CreateFactoryAsync();
        using var owner = AuthenticatedClient(factory, "customer", Guid.NewGuid().ToString());
        var body = ValidBody();
        var quoteResponse = await owner.PostAsJsonAsync("/api/v1/shipments/quote", body);
        quoteResponse.EnsureSuccessStatusCode();
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<QuoteDto>())!;
        var subject = new JwtSecurityTokenHandler().ReadJwtToken(owner.DefaultRequestHeaders.Authorization!.Parameter!).Subject;
        var response = await owner.PostAsJsonAsync("/api/v1/shipments", new
        {
            body.Sender, body.Recipient, body.Parcel, OwnerSubject = "forged-subject", Price = 0.01m
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var shipment = (await response.Content.ReadFromJsonAsync<ShipmentDto>())!;
        shipment.OwnerSubject.Should().Be(subject);
        shipment.Price.Should().Be(quote.Price).And.BeGreaterThan(0.01m);
        shipment.Currency.Should().Be("EUR");
        shipment.TrackingNumber.Should().MatchRegex("^PF[A-F0-9]{24}$");
        response.Headers.Location!.ToString().Should().Be($"/api/v1/shipments/{shipment.Id}");
    }

    [Fact]
    public async Task Access_filters_lists_and_protects_shipment_and_pdf_label()
    {
        await using var factory = await CreateFactoryAsync();
        using var owner = AuthenticatedClient(factory, "customer", Guid.NewGuid().ToString());
        var created = await owner.PostAsJsonAsync("/api/v1/shipments", ValidBody());
        created.EnsureSuccessStatusCode();
        var shipment = (await created.Content.ReadFromJsonAsync<ShipmentDto>())!;
        var path = $"/api/v1/shipments/{shipment.Id}";
        (await owner.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK);
        var label = await owner.GetAsync(path + "/label");
        label.StatusCode.Should().Be(HttpStatusCode.OK);
        label.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        label.Content.Headers.ContentDisposition!.FileNameStar.Should().Be(shipment.TrackingNumber + ".pdf");
        Encoding.ASCII.GetString(await label.Content.ReadAsByteArrayAsync()).Should().StartWith("%PDF-");

        using var other = AuthenticatedClient(factory, "customer", Guid.NewGuid().ToString());
        (await other.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.GetAsync(path + "/label")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.GetFromJsonAsync<ShipmentDto[]>("/api/v1/shipments"))!.Should().BeEmpty();
        using var staff = AuthenticatedClient(factory, "operator", Guid.NewGuid().ToString());
        (await staff.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await staff.GetAsync(path + "/label")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await staff.GetFromJsonAsync<ShipmentDto[]>("/api/v1/shipments?limit=100"))!
            .Should().Contain(value => value.Id == shipment.Id);
    }

    [Fact]
    public async Task Invalid_parcel_returns_validation_problem_without_creating_shipment()
    {
        await using var factory = await CreateFactoryAsync();
        using var owner = AuthenticatedClient(factory, "customer", Guid.NewGuid().ToString());
        var body = ValidBody() with { Parcel = new ParcelInput(-1, 10, 10, 10) };
        var response = await owner.PostAsJsonAsync("/api/v1/shipments", body);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("traceId");
        (await owner.GetFromJsonAsync<ShipmentDto[]>("/api/v1/shipments"))!.Should().BeEmpty();
    }

    [Theory]
    [InlineData("a@b..c")]
    [InlineData("a b@example.test")]
    [InlineData("a\"b@example.test")]
    public async Task Invalid_email_returns_validation_problem_without_creating_shipment(string email)
    {
        await using var factory = await CreateFactoryAsync();
        using var owner = AuthenticatedClient(factory, "customer", Guid.NewGuid().ToString());
        var body = ValidBody();
        body = body with { Sender = body.Sender with { Email = email } };
        var response = await owner.PostAsJsonAsync("/api/v1/shipments", body);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await owner.GetFromJsonAsync<ShipmentDto[]>("/api/v1/shipments"))!.Should().BeEmpty();
    }

    [Theory]
    [InlineData("customer", HttpStatusCode.Forbidden)]
    [InlineData("operator", HttpStatusCode.Forbidden)]
    [InlineData("admin", HttpStatusCode.OK)]
    public async Task Only_admin_can_update_tariffs(string role, HttpStatusCode expected)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = AuthenticatedClient(factory, role, Guid.NewGuid().ToString());
        var tariffs = (await client.GetFromJsonAsync<TariffDto[]>("/api/v1/tariffs"))!;
        var tariff = tariffs.First();
        var response = await client.PutAsJsonAsync($"/api/v1/tariffs/{tariff.Zone}",
            new { tariff.BasePrice, tariff.PricePerKg });
        response.StatusCode.Should().Be(expected);
        if (role == "admin")
            (await client.PutAsJsonAsync("/api/v1/tariffs/999", new { BasePrice = 5m, PricePerKg = 1m }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Public_health_and_metrics_work_and_api_requires_authentication_and_supported_version()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/metrics")).StatusCode.Should().Be(HttpStatusCode.OK);
        var denied = await client.GetAsync("/api/v1/shipments");
        denied.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        denied.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var owner = AuthenticatedClient(factory, "customer", Guid.NewGuid().ToString());
        (await owner.GetAsync("/api/v2/shipments")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var missing = await owner.GetAsync($"/api/v1/shipments/{Guid.NewGuid()}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    private static ShipmentTestBody ValidBody() => new(
        new AddressInput("Marie Martin", "marie@example.test", "+33612345678", "10 rue de la Paix", null, "75002", "Paris", "FR"),
        new AddressInput("Paul Durand", "paul@example.test", "+33687654321", "20 rue Victor Hugo", null, "69002", "Lyon", "FR"),
        new ParcelInput(2, 20, 15, 10));

    private static HttpClient AuthenticatedClient(JwtApiFactory factory, string role, string subject)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(role, subject: subject));
        return client;
    }

    private sealed record ShipmentTestBody(AddressInput Sender, AddressInput Recipient, ParcelInput Parcel);

    [Theory]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData("customer", HttpStatusCode.OK)]
    [InlineData("operator", HttpStatusCode.OK)]
    public async Task Valid_signed_token_uses_production_realm_role_mapping(string role, HttpStatusCode expected)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(role));

        using var response = await client.GetAsync("/api/v1/tariffs");

        response.StatusCode.Should().Be(expected);
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("admin", fault));

        using var response = await client.GetAsync("/api/v1/tariffs");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    private async Task<JwtApiFactory> CreateFactoryAsync()
    {
        var factory = new JwtApiFactory(infrastructure);
        await factory.Services.InitializeShipmentDatabaseAsync();
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

    private sealed class JwtApiFactory(InfrastructureFixture dependencies) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:ShipmentDatabase"] = dependencies.Postgres.GetConnectionString(),
                    ["ConnectionStrings:Redis"] = dependencies.Redis.GetConnectionString(),
                    ["RabbitMq:Host"] = dependencies.RabbitMq.Hostname,
                    ["RabbitMq:Port"] = dependencies.RabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["RabbitMq:VirtualHost"] = "/",
                    ["RabbitMq:Username"] = "rabbitmq",
                    ["RabbitMq:Password"] = "rabbitmq",
                    ["Database:Initialize"] = "false",
                    ["Database:SeedDemoData"] = "true"
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
