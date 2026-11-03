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
using ParcelFlow.Billing.Application;
using ParcelFlow.Billing.Domain;
using ParcelFlow.Billing.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Billing.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class BillingApiTests(InfrastructureFixture infrastructure)
{
    private const string Issuer = "https://issuer.test";
    private const string Audience = "billing-api";
    private static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("ParcelFlow-test-only-signing-key-2026-never-for-production"));
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("operator", HttpStatusCode.Forbidden)]
    [InlineData("customer", HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    public async Task Financial_endpoints_are_restricted_to_customer_and_admin(string? role, HttpStatusCode expected)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = Client(factory, role);
        using var response = await client.GetAsync("/api/v1/billing/invoices");
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
        using var response = await client.GetAsync("/api/v1/billing/invoices");
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Customer_sees_only_own_invoices_and_cannot_read_or_pay_another_owner()
    {
        await using var factory = await CreateFactoryAsync();
        var owner = "owner-" + Guid.NewGuid().ToString("N");
        var invoiceId = await SeedInvoiceAsync(factory, owner);
        var otherId = await SeedInvoiceAsync(factory, "other-" + Guid.NewGuid().ToString("N"));
        using var client = Client(factory, "customer", owner);
        using var response = await client.GetAsync($"/api/v1/billing/invoices/{invoiceId}");
        response.EnsureSuccessStatusCode();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var page = JsonDocument.Parse(await client.GetStringAsync("/api/v1/billing/invoices?ownerSubject=forged"));
        page.RootElement.GetProperty("total").GetInt32().Should().Be(1);
        page.RootElement.GetProperty("items")[0].GetProperty("customerSubject").GetString().Should().Be(owner);
        foreach (var path in new[] { $"/invoices/{otherId}", $"/invoices/{otherId}/payment", $"/invoices/{otherId}/credit-notes", $"/invoices/{otherId}/credit-notes/{Guid.NewGuid()}" })
        {
            using var denied = await client.GetAsync("/api/v1/billing" + path);
            await AssertProblemAsync(denied, HttpStatusCode.Forbidden);
        }
        using var payment = await client.PostAsJsonAsync($"/api/v1/billing/invoices/{otherId}/payments", new { operationId = Guid.NewGuid(), outcome = "Approved" });
        await AssertProblemAsync(payment, HttpStatusCode.Forbidden);
        using var admin = Client(factory, "admin");
        (await admin.GetAsync($"/api/v1/billing/invoices/{otherId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Approved", "Paid")]
    [InlineData("Declined", "PaymentFailed")]
    public async Task Mock_payment_charges_server_amount_is_idempotent_and_terminal(string outcome, string status)
    {
        await using var factory = await CreateFactoryAsync();
        var owner = "payer-" + Guid.NewGuid().ToString("N");
        var id = await SeedInvoiceAsync(factory, owner, 19.95m);
        using var client = Client(factory, "customer", owner);
        var operationId = Guid.NewGuid();
        var body = new { operationId, outcome, amount = 0.01m, currency = "USD", customerSubject = "forged" };
        var path = $"/api/v1/billing/invoices/{id}/payments";
        using var response = await client.PostAsJsonAsync(path, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        using var payment = JsonDocument.Parse(text);
        payment.RootElement.GetProperty("amount").GetDecimal().Should().Be(19.95m);
        payment.RootElement.GetProperty("currency").GetString().Should().Be("EUR");
        payment.RootElement.GetProperty("outcome").GetString().Should().Be(outcome);
        using var replay = await client.PostAsJsonAsync(path, body);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).Should().Be(text);
        using var recorded = await client.GetAsync($"/api/v1/billing/invoices/{id}/payment");
        (await recorded.Content.ReadAsStringAsync()).Should().Be(text);
        using var second = await client.PostAsJsonAsync(path, new { operationId = Guid.NewGuid(), outcome });
        await AssertProblemAsync(second, HttpStatusCode.Conflict);
        using var changed = await client.PostAsJsonAsync(path, new { operationId, outcome = outcome == "Approved" ? "Declined" : "Approved" });
        await AssertProblemAsync(changed, HttpStatusCode.Conflict);
        using var invoice = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/billing/invoices/{id}"));
        invoice.RootElement.GetProperty("status").GetString().Should().Be(status);
    }

    [Fact]
    public async Task Only_admin_can_credit_paid_invoice_with_bounded_partial_credits()
    {
        await using var factory = await CreateFactoryAsync();
        var owner = "credit-owner-" + Guid.NewGuid().ToString("N");
        var id = await SeedInvoiceAsync(factory, owner, 20m);
        using var customer = Client(factory, "customer", owner);
        using var admin = Client(factory, "admin");
        var path = $"/api/v1/billing/invoices/{id}/credit-notes";
        using var unpaid = await admin.PostAsJsonAsync(path, new { operationId = Guid.NewGuid(), amount = 1m, reason = "ServiceIssue" });
        await AssertProblemAsync(unpaid, HttpStatusCode.Conflict);
        (await customer.PostAsJsonAsync($"/api/v1/billing/invoices/{id}/payments", new { operationId = Guid.NewGuid(), outcome = "Approved" })).EnsureSuccessStatusCode();
        var operationId = Guid.NewGuid();
        var body = new { operationId, amount = 7.50m, reason = "ServiceIssue" };
        using var denied = await customer.PostAsJsonAsync(path, body);
        await AssertProblemAsync(denied, HttpStatusCode.Forbidden);
        using var created = await admin.PostAsJsonAsync(path, body);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var text = await created.Content.ReadAsStringAsync();
        using var replay = await admin.PostAsJsonAsync(path, body);
        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        (await replay.Content.ReadAsStringAsync()).Should().Be(text);
        using var credit = await customer.GetAsync(created.Headers.Location);
        credit.StatusCode.Should().Be(HttpStatusCode.OK);
        using var page = JsonDocument.Parse(await customer.GetStringAsync(path));
        page.RootElement.GetProperty("total").GetInt32().Should().Be(1);
        using var changed = await admin.PostAsJsonAsync(path, new { operationId, amount = 8m, reason = "ServiceIssue" });
        await AssertProblemAsync(changed, HttpStatusCode.Conflict);
        using var over = await admin.PostAsJsonAsync(path, new { operationId = Guid.NewGuid(), amount = 12.51m, reason = "BillingCorrection" });
        await AssertProblemAsync(over, HttpStatusCode.Conflict);
        using var final = await admin.PostAsJsonAsync(path, new { operationId = Guid.NewGuid(), amount = 12.50m, reason = "BillingCorrection" });
        final.StatusCode.Should().Be(HttpStatusCode.Created);
        using var invoice = JsonDocument.Parse(await customer.GetStringAsync($"/api/v1/billing/invoices/{id}"));
        invoice.RootElement.GetProperty("status").GetString().Should().Be("Credited");
        invoice.RootElement.GetProperty("creditTotal").GetDecimal().Should().Be(20m);
        invoice.RootElement.GetProperty("remainingCredit").GetDecimal().Should().Be(0m);
    }

    [Theory]
    [InlineData("payments", "{}")]
    [InlineData("payments", "{\"operationId\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("payments", "{\"outcome\":\"Approved\"}")]
    [InlineData("payments", "{\"operationId\":\"00000000-0000-0000-0000-000000000001\",\"outcome\":\"unknown\"}")]
    [InlineData("payments", "{\"operationId\":\"00000000-0000-0000-0000-000000000001\",\"outcome\":99}")]
    [InlineData("payments", "{\"operationId\":\"00000000-0000-0000-0000-000000000000\",\"outcome\":\"Approved\"}")]
    [InlineData("credit-notes", "{}")]
    [InlineData("credit-notes", "{\"operationId\":\"00000000-0000-0000-0000-000000000001\",\"reason\":\"ServiceIssue\"}")]
    [InlineData("credit-notes", "{\"operationId\":\"00000000-0000-0000-0000-000000000001\",\"amount\":1}")]
    [InlineData("credit-notes", "{\"amount\":1,\"reason\":\"ServiceIssue\"}")]
    [InlineData("credit-notes", "{\"operationId\":\"00000000-0000-0000-0000-000000000001\",\"amount\":1.001,\"reason\":\"ServiceIssue\"}")]
    [InlineData("credit-notes", "{\"operationId\":\"00000000-0000-0000-0000-000000000001\",\"amount\":0,\"reason\":\"ServiceIssue\"}")]
    [InlineData("credit-notes", "{\"operationId\":\"00000000-0000-0000-0000-000000000001\",\"amount\":1,\"reason\":99}")]
    public async Task Missing_or_invalid_financial_decisions_are_bad_requests(string resource, string body)
    {
        await using var factory = await CreateFactoryAsync();
        var id = await SeedInvoiceAsync(factory, "input-test");
        using var admin = Client(factory, "admin");
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await admin.PostAsync($"/api/v1/billing/invoices/{id}/{resource}", content);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Health_metrics_openapi_and_query_validation_are_available()
    {
        await using var factory = new JwtApiFactory(infrastructure, "Development");
        await factory.Services.InitializeBillingDatabaseAsync();
        using var client = Client(factory, "admin");
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/metrics")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v2/billing/invoices")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var badPage = await client.GetAsync("/api/v1/billing/invoices?page=0");
        await AssertProblemAsync(badPage, HttpStatusCode.BadRequest);
        using var absent = await client.GetAsync($"/api/v1/billing/invoices/{Guid.NewGuid()}");
        await AssertProblemAsync(absent, HttpStatusCode.NotFound);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        schemas.GetProperty("MockPaymentOutcome").GetProperty("type").GetString().Should().Be("string");
        schemas.GetProperty("CreditReason").GetProperty("type").GetString().Should().Be("string");
        foreach (var (name, field) in new[] { ("PayInvoiceBody", "operationId"), ("PayInvoiceBody", "outcome"), ("IssueCreditNoteBody", "operationId"), ("IssueCreditNoteBody", "amount"), ("IssueCreditNoteBody", "reason") })
            schemas.GetProperty(name).GetProperty("required").EnumerateArray().Select(x => x.GetString()).Should().Contain(field);
    }

    private static async Task<Guid> SeedInvoiceAsync(JwtApiFactory factory, string subject, decimal amount = 12.50m)
    {
        var id = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RegisterInvoice(id, subject, amount, "EUR", DateTimeOffset.UtcNow));
        return id;
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
        await factory.Services.InitializeBillingDatabaseAsync();
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
                    ["ConnectionStrings:BillingDatabase"] = dependencies.Postgres.GetConnectionString(),
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
