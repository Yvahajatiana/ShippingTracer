using System.Security.Claims;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ParcelFlow.BuildingBlocks.Api.Correlation;
using ParcelFlow.BuildingBlocks.Api.Errors;
using ParcelFlow.Customer.Api;
using ParcelFlow.Customer.Application;
using ParcelFlow.Customer.Infrastructure;
using ParcelFlow.Customer.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext().WriteTo.Console());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddCustomerApplication();
builder.Services.AddCustomerInfrastructure(builder.Configuration);
builder.Services.AddExceptionHandler<CustomerExceptionHandler>();
builder.Services.AddParcelFlowProblemDetails();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.Authority = builder.Configuration["Keycloak:Authority"];
    options.Audience = builder.Configuration["Keycloak:Audience"];
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.MapInboundClaims = false;
    options.TokenValidationParameters.NameClaimType = "preferred_username";
    options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            if (string.IsNullOrWhiteSpace(context.Principal?.FindFirst("sub")?.Value))
            {
                context.Fail("Missing subject claim.");
                return Task.CompletedTask;
            }
            if (context.Principal?.Identity is ClaimsIdentity identity &&
                context.Principal.FindFirst("realm_access") is { } realmAccess)
            {
                try
                {
                    using var document = JsonDocument.Parse(realmAccess.Value);
                    if (document.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var role in roles.EnumerateArray())
                        {
                            if (role.ValueKind == JsonValueKind.String && role.GetString() is { } name)
                            {
                                identity.AddClaim(new Claim(ClaimTypes.Role, name));
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    context.Fail("Invalid realm role claims.");
                }
            }
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("admin", policy => policy.RequireAuthenticatedUser().RequireRole("admin"));
    options.AddPolicy("operations", policy => policy.RequireAuthenticatedUser().RequireRole("admin", "operator"));
    options.AddPolicy("customer-or-staff", policy => policy.RequireAuthenticatedUser().RequireRole("customer", "operator", "admin"));
});
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
}).AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ParcelFlow Customer API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        Description = "Keycloak access token"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
    });
});
var telemetryEndpoint = builder.Configuration["OpenTelemetry:Endpoint"];
builder.Services.AddOpenTelemetry().ConfigureResource(resource => resource.AddService("parcelflow-customer"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(telemetryEndpoint))
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(telemetryEndpoint));
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddPrometheusExporter();
        if (!string.IsNullOrWhiteSpace(telemetryEndpoint))
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(telemetryEndpoint));
    });

var app = builder.Build();
app.UseParcelFlowCorrelation();
app.UseParcelFlowProblemDetails();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    if (app.Configuration.GetValue<bool>("Database:Initialize"))
    {
        await app.Services.InitializeCustomerDatabaseAsync();
    }
}
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapPrometheusScrapingEndpoint().AllowAnonymous();
app.MapCustomerEndpoints();
app.Run();

public partial class Program;
