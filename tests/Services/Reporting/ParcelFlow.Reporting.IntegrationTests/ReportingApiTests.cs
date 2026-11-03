
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
using Npgsql;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ParcelFlow.Reporting.Application;
using ParcelFlow.Reporting.Domain;
using ParcelFlow.Reporting.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Reporting.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class ReportingApiTests(InfrastructureFixture infrastructure)
{
    private const string Issuer = "https://issuer.test";
    private const string Audience = "reporting-api";
    private static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("ParcelFlow-test-only-signing-key-2026-never-for-production"));

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("customer", HttpStatusCode.Forbidden)]
    [InlineData("operator", HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    public async Task Aggregates_require_operator_or_admin(string? role, HttpStatusCode expected)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory, role);
        using var response = await client.GetAsync("/api/v1/reports/kpis?from=2026-10-01&to=2026-11-01");
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
    [InlineData("blank-subject")]
    [InlineData("padded-subject")]
    [InlineData("long-subject")]
    [InlineData("control-subject")]
    [InlineData("missing-realm")]
    public async Task Invalid_token_returns_unauthorized_problem_details(string fault)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken("admin", fault));
        using var response = await client.GetAsync("/api/v1/reports/kpis?from=2026-10-01&to=2026-11-01");
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Role_claim_outside_realm_access_cannot_grant_reporting_access()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken("customer", "forged-role"));
        using var response = await client.GetAsync("/api/v1/reports/kpis?from=2026-10-01&to=2026-11-01");
        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Oversized_request_returns_no_store_problem_details()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory, "admin");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/reports/kpis?from=2026-10-01&to=2026-11-01")
        {
            Content = new ByteArrayContent(new byte[1024 * 1024 + 1])
        };
        using var response = await client.SendAsync(request);
        await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("?from=2026-10-01")]
    [InlineData("?to=2026-11-01")]
    [InlineData("?from=invalid&to=2026-11-01")]
    [InlineData("?from=2026-02-30&to=2026-11-01")]
    [InlineData("?from=2026-10-01&to=invalid")]
    [InlineData("?from=2026-10-01&to=2026-10-01")]
    [InlineData("?from=2026-10-02&to=2026-10-01")]
    [InlineData("?from=2024-01-01&to=2025-01-02")]
    public async Task Invalid_period_returns_json_problem_details_even_when_xml_requested(string query)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory, "admin");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/xml");
        using var response = await client.GetAsync("/api/v1/reports/kpis" + query);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Leap_year_period_of_exactly_366_days_is_allowed()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory, "operator");
        using var response = await client.GetAsync("/api/v1/reports/kpis?from=2024-01-01&to=2025-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("dailyVolumes").GetArrayLength().Should().Be(366);
    }

    [Fact]
    public async Task Empty_future_cohort_has_null_rate_and_durations_and_all_status_buckets()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory, "operator");
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/v1/reports/kpis?from=2090-01-01&to=2090-01-02"));
        var report = json.RootElement;
        report.GetProperty("totalShipments").GetInt64().Should().Be(0);
        report.GetProperty("deliveredShipments").GetInt64().Should().Be(0);
        report.GetProperty("deliveryRatePercent").ValueKind.Should().Be(JsonValueKind.Null);
        report.GetProperty("averageDeliveryHours").ValueKind.Should().Be(JsonValueKind.Null);
        report.GetProperty("averageTransitHours").ValueKind.Should().Be(JsonValueKind.Null);
        report.GetProperty("deliveryDurationSamples").GetInt64().Should().Be(0);
        report.GetProperty("transitDurationSamples").GetInt64().Should().Be(0);
        report.GetProperty("dailyVolumes").GetArrayLength().Should().Be(1);
        var statuses = report.GetProperty("statusVolumes").EnumerateArray().ToArray();
        statuses.Should().HaveCount(8);
        statuses.Should().OnlyContain(x => x.GetProperty("shipments").GetInt64() == 0);
        statuses.Select(x => x.GetProperty("status").GetString()).Should().BeEquivalentTo(Enum.GetNames<ParcelStatus>());
    }

    [Fact]
    public async Task Cohort_is_half_open_and_response_contains_only_aggregate_data()
    {
        await using var factory = await CreateFactoryAsync();
        var origin = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var excluded = Guid.NewGuid();
        await RecordAsync(factory, first, ParcelStatus.Created, origin);
        await RecordAsync(factory, first, ParcelStatus.PickedUp, origin.AddHours(2));
        await RecordAsync(factory, first, ParcelStatus.Delivered, origin.AddHours(26));
        await RecordAsync(factory, second, ParcelStatus.Created, origin.AddDays(1));
        await RecordAsync(factory, second, ParcelStatus.PickedUp, origin.AddDays(1).AddHours(4));
        await RecordAsync(factory, second, ParcelStatus.Delivered, origin.AddDays(1).AddHours(28));
        await RecordAsync(factory, third, ParcelStatus.Created, origin.AddDays(2));
        await RecordAsync(factory, third, ParcelStatus.InTransit, origin.AddDays(2).AddHours(1));
        await RecordAsync(factory, excluded, ParcelStatus.Created, origin.AddMonths(1));
        using var client = Client(factory, "admin");
        using var response = await client.GetAsync("/api/v1/reports/kpis?from=2026-10-01&to=2026-11-01");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var report = json.RootElement;
        report.GetProperty("from").GetString().Should().Be("2026-10-01");
        report.GetProperty("to").GetString().Should().Be("2026-11-01");
        report.GetProperty("generatedAtUtc").GetDateTimeOffset().Offset.Should().Be(TimeSpan.Zero);
        report.GetProperty("totalShipments").GetInt64().Should().Be(3);
        report.GetProperty("deliveredShipments").GetInt64().Should().Be(2);
        report.GetProperty("deliveryRatePercent").GetDecimal().Should().Be(66.67m);
        report.GetProperty("averageDeliveryHours").GetDecimal().Should().Be(27m);
        report.GetProperty("deliveryDurationSamples").GetInt64().Should().Be(2);
        report.GetProperty("averageTransitHours").GetDecimal().Should().Be(24m);
        report.GetProperty("transitDurationSamples").GetInt64().Should().Be(2);
        var daily = report.GetProperty("dailyVolumes").EnumerateArray().ToArray();
        daily.Should().HaveCount(31);
        daily.Sum(x => x.GetProperty("shipments").GetInt64()).Should().Be(3);
        daily.Take(3).Should().OnlyContain(x => x.GetProperty("shipments").GetInt64() == 1);
        var statuses = report.GetProperty("statusVolumes").EnumerateArray().ToArray();
        statuses.Single(x => x.GetProperty("status").GetString() == "Delivered").GetProperty("shipments").GetInt64().Should().Be(2);
        statuses.Single(x => x.GetProperty("status").GetString() == "InTransit").GetProperty("shipments").GetInt64().Should().Be(1);
        body.Should().NotContain(first.ToString()).And.NotContain(second.ToString()).And.NotContain(third.ToString())
            .And.NotContain(excluded.ToString()).And.NotContain("trackingNumber").And.NotContain("customerSubject")
            .And.NotContain("sourceEventId").And.NotContain("shipmentId");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Health_metrics_versioning_and_read_only_date_and_string_enum_openapi_are_available()
    {
        await using var factory = new JwtApiFactory(infrastructure, "Development");
        await factory.Services.InitializeReportingDatabaseAsync();
        using var client = Client(factory, "admin");
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/metrics")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v2/reports/kpis?from=2026-10-01&to=2026-11-01")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var post = await client.PostAsJsonAsync("/api/v1/reports/kpis", new { from = "2026-10-01", to = "2026-11-01" });
        post.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var path = json.RootElement.GetProperty("paths").EnumerateObject().Single();
        path.Name.Should().Be("/api/v1/reports/kpis");
        path.Value.EnumerateObject().Select(x => x.Name).Should().Equal("get");
        var parameters = path.Value.GetProperty("get").GetProperty("parameters").EnumerateArray().ToArray();
        parameters.Should().HaveCount(2);
        parameters.Should().OnlyContain(x => x.GetProperty("required").GetBoolean() && x.GetProperty("schema").GetProperty("format").GetString() == "date");
        var schema = json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ParcelStatus");
        schema.GetProperty("type").GetString().Should().Be("string");
        schema.GetProperty("enum").EnumerateArray().Select(x => x.GetString()).Should().BeEquivalentTo(Enum.GetNames<ParcelStatus>());
    }

    private static async Task RecordAsync(JwtApiFactory factory, Guid shipment, ParcelStatus status, DateTimeOffset occurredAt)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RecordStatusFact(
            Guid.NewGuid(), Guid.NewGuid(), shipment, "PF" + shipment.ToString("N")[..24].ToUpperInvariant(), status, occurredAt, null));
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
        await factory.Services.InitializeReportingDatabaseAsync();
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
        if (fault == "missing-realm") claims.Clear();
        if (fault == "forged-role") claims.Add(new Claim(ClaimTypes.Role, "admin"));
        subject = fault switch
        {
            "blank-subject" => "   ",
            "padded-subject" => " customer ",
            "long-subject" => new string('s', 201),
            "control-subject" => "customer\nname",
            _ => subject
        };
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
        private readonly string _database = "reporting_api_" + Guid.NewGuid().ToString("N");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:ReportingDatabase"] = new NpgsqlConnectionStringBuilder(dependencies.Postgres.GetConnectionString()) { Database = _database }.ConnectionString,
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
