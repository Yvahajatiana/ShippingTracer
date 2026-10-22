using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParcelFlow.Customer.Application;
using ParcelFlow.Customer.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Customer.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class CustomerApiTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Unauthenticated_request_returns_problem_details()
    {
        await using var factory = await CreateFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/customers/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Customer_can_manage_own_profile_but_cannot_read_others_or_list_customers()
    {
        await using var factory = await CreateFactory();
        using var owner = factory.CreateClient();
        Authenticate(owner, Guid.NewGuid().ToString(), "customer");
        var created = await owner.PostAsJsonAsync("/api/v1/customers", new CreateProfile("Marie", "Martin", "marie@example.test", null));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var profile = await created.Content.ReadFromJsonAsync<ProfileDto>();
        profile.Should().NotBeNull();
        (await owner.GetAsync($"/api/v1/customers/{profile!.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.GetAsync("/api/v1/customers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var other = factory.CreateClient();
        Authenticate(other, Guid.NewGuid().ToString(), "customer");
        (await other.GetAsync($"/api/v1/customers/{profile.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var staff = factory.CreateClient();
        Authenticate(staff, Guid.NewGuid().ToString(), "operator");
        (await staff.GetAsync($"/api/v1/customers/{profile.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await staff.PutAsJsonAsync($"/api/v1/customers/{profile.Id}",
            new { FirstName = "Changed", LastName = "Martin", Email = "marie@example.test", Phone = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invalid_profile_is_rejected_without_persisting_customer()
    {
        await using var factory = await CreateFactory();
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid().ToString();
        Authenticate(client, subject, "customer");
        var response = await client.PostAsJsonAsync("/api/v1/customers", new CreateProfile("", "Martin", "invalid", null));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers
            .AnyAsync(customer => customer.KeycloakSubject == subject)).Should().BeFalse();
    }

    [Fact]
    public async Task Oversized_subject_rejected_by_the_domain_returns_a_validation_problem()
    {
        await using var factory = await CreateFactory();
        using var client = factory.CreateClient();
        var subject = new string('s', 201);
        Authenticate(client, subject, "customer");
        var response = await client.PostAsJsonAsync("/api/v1/customers", new CreateProfile("Marie", "Martin", "marie@example.test", null));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"keycloakSubject\"").And.NotContain("between 1 and 200");
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers
            .AnyAsync(customer => customer.KeycloakSubject == subject)).Should().BeFalse();
    }

    [Fact]
    public async Task Email_surrounded_by_whitespace_is_trimmed_before_persistence()
    {
        await using var factory = await CreateFactory();
        using var client = factory.CreateClient();
        Authenticate(client, Guid.NewGuid().ToString(), "customer");
        var created = await client.PostAsJsonAsync("/api/v1/customers", new CreateProfile("Marie", "Martin", "  marie@example.test ", null));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var profile = await created.Content.ReadFromJsonAsync<ProfileDto>();
        profile!.Email.Should().Be("marie@example.test");
        var updated = await client.PutAsJsonAsync($"/api/v1/customers/{profile.Id}",
            new { FirstName = "Marie", LastName = "Martin", Email = " marie.martin@example.test\t", Phone = (string?)null });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<ProfileDto>())!.Email.Should().Be("marie.martin@example.test");
    }

    [Fact]
    public async Task Customer_identity_cannot_register_a_second_profile()
    {
        await using var factory = await CreateFactory();
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid().ToString();
        Authenticate(client, subject, "customer");
        var request = new CreateProfile("Marie", "Martin", "marie@example.test", null);
        (await client.PostAsJsonAsync("/api/v1/customers", request)).StatusCode.Should().Be(HttpStatusCode.Created);
        var duplicate = await client.PostAsJsonAsync("/api/v1/customers", request);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers
            .CountAsync(customer => customer.KeycloakSubject == subject)).Should().Be(1);
    }

    [Fact]
    public async Task Address_can_be_created_updated_and_deleted_by_customer()
    {
        await using var factory = await CreateFactory();
        using var client = factory.CreateClient();
        Authenticate(client, Guid.NewGuid().ToString(), "customer");
        var createdProfile = await client.PostAsJsonAsync("/api/v1/customers", new CreateProfile("Marie", "Martin", "marie@example.test", null));
        createdProfile.EnsureSuccessStatusCode();
        var profile = (await createdProfile.Content.ReadFromJsonAsync<ProfileDto>())!;
        var body = new { Label = "Domicile", Line1 = "10 rue de la Paix", Line2 = (string?)null,
            PostalCode = "75002", City = "Paris", CountryCode = "FR" };
        var created = await client.PostAsJsonAsync($"/api/v1/customers/{profile.Id}/addresses", body);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var address = (await created.Content.ReadFromJsonAsync<AddressDto>())!;
        var path = $"/api/v1/customers/{profile.Id}/addresses/{address.Id}";
        (await client.PutAsJsonAsync(path, body with { City = "Lyon", PostalCode = "69002" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Addresses.SingleAsync(value => value.Id == address.Id);
            stored.CustomerId.Should().Be(profile.Id);
            stored.City.Should().Be("Lyon");
        }
        (await client.DeleteAsync(path)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using var verify = factory.Services.CreateAsyncScope();
        (await verify.ServiceProvider.GetRequiredService<CustomerDbContext>().Addresses.AnyAsync(value => value.Id == address.Id)).Should().BeFalse();
    }

    [Theory]
    [InlineData("customer", HttpStatusCode.Forbidden)]
    [InlineData("operator", HttpStatusCode.Forbidden)]
    [InlineData("admin", HttpStatusCode.Created)]
    public async Task Only_admin_can_create_companies(string role, HttpStatusCode expected)
    {
        await using var factory = await CreateFactory();
        using var client = factory.CreateClient();
        Authenticate(client, Guid.NewGuid().ToString(), role);
        var response = await client.PostAsJsonAsync("/api/v1/companies",
            new CreateCompany("Atelier Martin", Guid.NewGuid().ToString(), "contact@example.test"));
        response.StatusCode.Should().Be(expected);
    }

    private async Task<CustomerApiFactory> CreateFactory()
    {
        var factory = new CustomerApiFactory(infrastructure);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Database.MigrateAsync();
        return factory;
    }

    private static void Authenticate(HttpClient client, string subject, string role)
    {
        client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
    }
}

internal sealed class CustomerApiFactory(InfrastructureFixture infrastructure) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CustomerDatabase"] = infrastructure.Postgres.GetConnectionString(),
            ["ConnectionStrings:Redis"] = infrastructure.Redis.GetConnectionString(),
            ["RabbitMq:Host"] = infrastructure.RabbitMq.Hostname,
            ["RabbitMq:Port"] = infrastructure.RabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:VirtualHost"] = "/",
            ["RabbitMq:Username"] = "rabbitmq",
            ["RabbitMq:Password"] = "rabbitmq",
            ["Database:Initialize"] = "false",
            ["Database:SeedDemoData"] = "false"
        }));
        builder.ConfigureServices(services =>
        {
            services.AddDbContext<CustomerDbContext>(options => options.UseNpgsql(infrastructure.Postgres.GetConnectionString()));
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                options.DefaultForbidScheme = TestAuthenticationHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
        });
    }
}

internal sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public new const string Scheme = "CustomerTests";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Subject", out var subject))
            return Task.FromResult(AuthenticateResult.NoResult());
        Claim[] claims = [new("sub", subject.ToString()), new(ClaimTypes.NameIdentifier, subject.ToString()),
            new(ClaimTypes.Role, Request.Headers["X-Test-Role"].ToString())];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
    }
}
