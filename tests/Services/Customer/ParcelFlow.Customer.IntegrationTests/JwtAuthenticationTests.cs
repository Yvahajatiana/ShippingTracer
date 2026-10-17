using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ParcelFlow.Customer.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Customer.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class JwtAuthenticationTests(InfrastructureFixture infrastructure)
{
    private const string Issuer = "https://issuer.test";
    private const string Audience = "customer-api";
    private static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("ParcelFlow-test-only-signing-key-2026-never-for-production"));

    [Theory]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData("customer", HttpStatusCode.Forbidden)]
    public async Task Valid_signed_token_uses_production_realm_role_mapping(string role, HttpStatusCode expected)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(role));

        using var response = await client.GetAsync("/api/v1/companies");

        response.StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("missing-subject")]
    public async Task Invalid_signed_token_is_rejected(string fault)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("admin", fault));

        using var response = await client.GetAsync("/api/v1/companies");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    private async Task<JwtApiFactory> CreateFactoryAsync()
    {
        var factory = new JwtApiFactory(infrastructure);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Database.MigrateAsync();
        return factory;
    }

    private static string CreateToken(string role, string? fault = null)
    {
        var now = DateTime.UtcNow;
        List<Claim> claims =
        [
            new("realm_access", JsonSerializer.Serialize(new { roles = new[] { role } }), JsonClaimValueTypes.Json)
        ];
        if (fault != "missing-subject") claims.Add(new Claim("sub", Guid.NewGuid().ToString()));
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
                    ["ConnectionStrings:CustomerDatabase"] = dependencies.Postgres.GetConnectionString(),
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
