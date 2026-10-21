using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ParcelFlow.Gateway.Tests;

public sealed class GatewayTests
{
    private const string Issuer = "https://issuer.gateway.test";
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("Gateway-test-only-signing-key-never-use-in-production-2026"));

    [Theory]
    [InlineData("Keycloak:Authority", "http://insecure.test")]
    [InlineData("Gateway:Destinations:Customer", "file:///etc/passwd")]
    [InlineData("Gateway:CorsOrigins:0", "https://*.example.test")]
    public void Invalid_runtime_configuration_prevents_startup(string key, string value)
    {
        using var factory = new GatewayFactory("http://127.0.0.1:1", new() { [key] = value });
        FluentActions.Invoking(() => factory.CreateClient()).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("customers", "customer-api")]
    [InlineData("companies", "customer-api")]
    [InlineData("shipments", "shipment-api")]
    [InlineData("tariffs", "shipment-api")]
    [InlineData("tracking/shipments/123", "tracking-api")]
    [InlineData("warehouses", "warehouse-api")]
    [InlineData("delivery", "delivery-api")]
    [InlineData("billing", "billing-api")]
    [InlineData("notifications", "notification-api")]
    [InlineData("reports", "reporting-api")]
    public async Task Routes_preserve_path_query_and_require_the_service_audience(string path, string audience)
    {
        await using var upstream = await Upstream.StartAsync(context => context.Response.WriteAsync(context.Request.Path + context.Request.QueryString));
        await using var factory = new GatewayFactory(upstream.Url);
        using var client = factory.CreateClient();
        using var wrong = Request(HttpMethod.Get, "/api/v1/" + path, Token(audience == "customer-api" ? "reporting-api" : "customer-api"));
        (await client.SendAsync(wrong)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var correct = Request(HttpMethod.Get, "/api/v1/" + path + "?search=a%20b&take=2", Token(audience));
        using var response = await client.SendAsync(correct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("/api/v1/" + path + "?search=a%20b&take=2");
        upstream.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("malformed")]
    [InlineData("signature")]
    [InlineData("expired")]
    [InlineData("audience")]
    public async Task Protected_routes_reject_untrusted_credentials_before_forwarding(string fault)
    {
        await using var upstream = await Upstream.StartAsync(context => context.Response.WriteAsync("unexpected"));
        await using var factory = new GatewayFactory(upstream.Url);
        using var client = factory.CreateClient();
        var token = fault switch { "absent" => null, "malformed" => "invalid-token", "audience" => Token("unrelated-api"), _ => Token("customer-api", fault) };
        using var request = Request(HttpMethod.Get, "/api/v1/customers", token);
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        upstream.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Distinct_services_use_their_configured_destination_and_accept_multiple_audiences()
    {
        await using var customer = await Upstream.StartAsync(context => context.Response.WriteAsync("customer"));
        await using var shipment = await Upstream.StartAsync(context => context.Response.WriteAsync("shipment"));
        await using var factory = new GatewayFactory(customer.Url, new() { ["Gateway:Destinations:Shipment"] = shipment.Url });
        using var client = factory.CreateClient();
        var token = new JwtSecurityToken(Issuer, claims: [new Claim("sub", "gateway-test"), new Claim("aud", "customer-api"), new Claim("aud", "shipment-api")],
            notBefore: DateTime.UtcNow.AddMinutes(-1), expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        (await client.GetStringAsync("/api/v1/customers")).Should().Be("customer");
        (await client.GetStringAsync("/api/v1/shipments")).Should().Be("shipment");
        customer.Calls.Should().Be(1);
        shipment.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Only_the_tracking_lookup_is_public_and_only_for_get()
    {
        await using var upstream = await Upstream.StartAsync(context => context.Response.WriteAsync("tracking"));
        await using var factory = new GatewayFactory(upstream.Url);
        using var client = factory.CreateClient();
        (await client.GetAsync("/api/v1/tracking/PF123")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/tracking/shipments/123")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsync("/api/v1/tracking/PF123", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        upstream.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("/api/v1/unknown")]
    [InlineData("/api/v1/customers-other")]
    [InlineData("/api/v2/customers")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task Unknown_routes_do_not_reach_upstream(string path)
    {
        await using var upstream = await Upstream.StartAsync(context => context.Response.WriteAsync("unexpected"));
        await using var factory = new GatewayFactory(upstream.Url);
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Get, path, Token("customer-api"));
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        upstream.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Post_preserves_body_and_bearer_and_forwards_downstream_error_once()
    {
        var token = Token("shipment-api");
        await using var upstream = await Upstream.StartAsync(async context =>
        {
            context.Request.Headers.Authorization.ToString().Should().Be("Bearer " + token);
            (await new StreamReader(context.Request.Body).ReadToEndAsync()).Should().Be("{\"reference\":\"PF123\"}");
            context.Response.StatusCode = 409;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync("{\"status\":409,\"title\":\"duplicate\"}");
        });
        await using var factory = new GatewayFactory(upstream.Url);
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, "/api/v1/shipments", token);
        request.Content = new StringContent("{\"reference\":\"PF123\"}", Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("duplicate");
        upstream.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Binary_download_content_headers_and_location_are_preserved()
    {
        byte[] bytes = [0, 255, 10, 13, 42];
        await using var upstream = await Upstream.StartAsync(async context =>
        {
            context.Response.StatusCode = 201;
            context.Response.Headers.Location = "/api/v1/billing/invoices/123";
            context.Response.Headers.ContentDisposition = "attachment; filename=invoice.pdf";
            context.Response.ContentType = "application/pdf";
            await context.Response.Body.WriteAsync(bytes);
        });
        await using var factory = new GatewayFactory(upstream.Url);
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Get, "/api/v1/billing/invoices/123", Token("billing-api"));
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().Be("/api/v1/billing/invoices/123");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be("invoice.pdf");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
    }

    [Fact]
    public async Task Unavailable_upstream_returns_bad_gateway_problem()
    {
        var upstream = await Upstream.StartAsync(context => context.Response.WriteAsync("unused"));
        var url = upstream.Url;
        await upstream.DisposeAsync();
        await using var factory = new GatewayFactory(url);
        using var client = factory.CreateClient();
        await AssertProblem(await client.GetAsync("/api/v1/tracking/PF123"), HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Inactive_upstream_returns_gateway_timeout_problem()
    {
        await using var upstream = await Upstream.StartAsync(async context =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        });
        await using var factory = new GatewayFactory(upstream.Url, new() { ["Gateway:InactivityTimeoutSeconds"] = "1" });
        using var client = factory.CreateClient();
        await AssertProblem(await client.GetAsync("/api/v1/tracking/PF123"), HttpStatusCode.GatewayTimeout);
    }

    [Theory]
    [InlineData("https://portal.test", true)]
    [InlineData("https://untrusted.test", false)]
    public async Task Cors_preflight_only_allows_configured_origins(string origin, bool allowed)
    {
        await using var factory = new GatewayFactory("http://127.0.0.1:1", new() { ["Gateway:CorsOrigins:0"] = "https://portal.test" });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/customers");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        using var response = await client.SendAsync(request);
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins).Should().Be(allowed);
        if (allowed) origins.Should().Equal(origin);
    }

    [Fact]
    public async Task Public_rate_limit_returns_retry_after_without_forwarding_excess_request()
    {
        await using var upstream = await Upstream.StartAsync(context => context.Response.WriteAsync("tracking"));
        await using var factory = new GatewayFactory(upstream.Url, new() { ["Gateway:PublicPermitLimit"] = "1" });
        using var client = factory.CreateClient();
        (await client.GetAsync("/api/v1/tracking/PF123")).StatusCode.Should().Be(HttpStatusCode.OK);
        using var rejected = await client.GetAsync("/api/v1/tracking/PF456");
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();
        upstream.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Private_quota_is_shared_across_routes_for_a_subject_but_independent_between_subjects()
    {
        await using var upstream = await Upstream.StartAsync(context => context.Response.WriteAsync("accepted"));
        await using var factory = new GatewayFactory(upstream.Url, new() { ["Gateway:PrivatePermitLimit"] = "1" });
        using var client = factory.CreateClient();
        using var first = Request(HttpMethod.Get, "/api/v1/customers", Token("customer-api", subject: "first-customer"));
        (await client.SendAsync(first)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var exhausted = Request(HttpMethod.Get, "/api/v1/companies", Token("customer-api", subject: "first-customer"));
        using var rejection = await client.SendAsync(exhausted);
        rejection.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejection.Headers.RetryAfter.Should().NotBeNull();
        using var independent = Request(HttpMethod.Get, "/api/v1/customers", Token("customer-api", subject: "second-customer"));
        (await client.SendAsync(independent)).StatusCode.Should().Be(HttpStatusCode.OK);
        upstream.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Global_ip_quota_counts_missing_and_invalid_credentials()
    {
        await using var upstream = await Upstream.StartAsync(context => context.Response.WriteAsync("unexpected"));
        await using var factory = new GatewayFactory(upstream.Url, new() { ["Gateway:IpPermitLimit"] = "2" });
        using var client = factory.CreateClient();
        (await client.GetAsync("/api/v1/customers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var invalid = Request(HttpMethod.Get, "/api/v1/customers", "invalid-token");
        (await client.SendAsync(invalid)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var valid = Request(HttpMethod.Get, "/api/v1/customers", Token("customer-api"));
        using var rejected = await client.SendAsync(valid);
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();
        upstream.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Cors_exposes_download_location_and_retry_headers_to_the_configured_frontend()
    {
        await using var upstream = await Upstream.StartAsync(context => context.Response.WriteAsync("accepted"));
        await using var factory = new GatewayFactory(upstream.Url, new() { ["Gateway:CorsOrigins:0"] = "https://portal.test" });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tracking/PF123");
        request.Headers.Add("Origin", "https://portal.test");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("https://portal.test");
        var exposed = response.Headers.GetValues("Access-Control-Expose-Headers")
            .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        exposed.Should().Contain(value => value.Equals("Location", StringComparison.OrdinalIgnoreCase))
            .And.Contain(value => value.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase))
            .And.Contain(value => value.Equals("Retry-After", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Spoofed_forwarding_headers_are_not_trusted_by_upstream()
    {
        await using var upstream = await Upstream.StartAsync(async context =>
        {
            var headers = string.Join("|", context.Request.Headers.Where(x => x.Key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) || x.Key.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value.ToString()));
            await context.Response.WriteAsync(headers);
        });
        await using var factory = new GatewayFactory(upstream.Url);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tracking/PF123");
        request.Headers.Add("X-Forwarded-For", "203.0.113.77");
        request.Headers.Add("X-Forwarded-Host", "attacker.test");
        request.Headers.Add("Forwarded", "for=203.0.113.77;host=attacker.test");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("203.0.113.77").And.NotContain("attacker.test");
    }

    [Fact]
    public async Task Oversized_body_is_rejected_before_forwarding()
    {
        await using var upstream = await Upstream.StartAsync(context => context.Response.WriteAsync("unexpected"));
        await using var factory = new GatewayFactory(upstream.Url);
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, "/api/v1/shipments", Token("shipment-api"));
        request.Content = new ByteArrayContent(new byte[12 * 1024 * 1024 + 1]);
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        upstream.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Operational_endpoints_are_local()
    {
        await using var upstream = await Upstream.StartAsync(context => context.Response.WriteAsync("unexpected"));
        await using var factory = new GatewayFactory(upstream.Url);
        using var client = factory.CreateClient();
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/metrics")).StatusCode.Should().Be(HttpStatusCode.OK);
        upstream.Calls.Should().Be(0);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? token)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new("Bearer", token);
        return request;
    }

    private static string Token(string audience, string? fault = null, string subject = "gateway-test")
    {
        var key = fault == "signature" ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes("Incorrect-test-signing-key-never-use-in-production-2026")) : Key;
        var token = new JwtSecurityToken(Issuer, audience, [new Claim("sub", subject)], DateTime.UtcNow.AddHours(-1),
            fault == "expired" ? DateTime.UtcNow.AddMinutes(-10) : DateTime.UtcNow.AddMinutes(5), new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        using (response)
        {
            response.StatusCode.Should().Be(status);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            document.RootElement.GetProperty("status").GetInt32().Should().Be((int)status);
        }
    }

    private sealed class GatewayFactory(string upstream, Dictionary<string, string?>? overrides = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                Dictionary<string, string?> values = new()
                {
                    ["Keycloak:Authority"] = Issuer,
                    ["Gateway:PublicPermitLimit"] = "1000",
                    ["Gateway:PrivatePermitLimit"] = "1000",
                    ["Gateway:IpPermitLimit"] = "1000"
                };
                foreach (var service in new[] { "Customer", "Shipment", "Tracking", "Warehouse", "Delivery", "Billing", "Notification", "Reporting" })
                    values["Gateway:Destinations:" + service] = upstream;
                if (overrides is not null) foreach (var pair in overrides) values[pair.Key] = pair.Value;
                config.AddInMemoryCollection(values);
            });
            builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
                metadata.SigningKeys.Add(Key);
                options.Authority = null;
                options.MetadataAddress = string.Empty;
                options.Configuration = metadata;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                options.TokenValidationParameters.ValidIssuer = Issuer;
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.IssuerSigningKey = Key;
                options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                options.TokenValidationParameters.ValidateLifetime = true;
                options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
            }));
        }
    }

    private sealed class Upstream(WebApplication app) : IAsyncDisposable
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public string Url => app.Urls.Single() + "/";
        public static async Task<Upstream> StartAsync(RequestDelegate handler)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            var upstream = new Upstream(app);
            app.Run(async context =>
            {
                Interlocked.Increment(ref upstream._calls);
                await handler(context);
            });
            await app.StartAsync();
            return upstream;
        }
        public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    }
}
