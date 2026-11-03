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
using ParcelFlow.Shipment.Api;
using ParcelFlow.Shipment.Application;
using ParcelFlow.Shipment.Infrastructure;
using ParcelFlow.Shipment.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext().WriteTo.Console());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddShipmentApplication();
builder.Services.AddShipmentInfrastructure(builder.Configuration);
builder.Services.AddExceptionHandler<ShipmentExceptionHandler>();
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
            if (context.Principal?.Identity is ClaimsIdentity identity)
            {
                var realmAccess = context.Principal.FindFirst("realm_access");
                if (realmAccess is null)
                {
                    context.Fail("Missing realm role claims.");
                    return Task.CompletedTask;
                }
                try
                {
                    using var document = JsonDocument.Parse(realmAccess.Value);
                    if (document.RootElement.ValueKind != JsonValueKind.Object ||
                        !document.RootElement.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array ||
                        roles.EnumerateArray().Any(role => role.ValueKind != JsonValueKind.String))
                    {
                        context.Fail("Invalid realm role claims.");
                        return Task.CompletedTask;
                    }
                    foreach (var role in roles.EnumerateArray())
                        identity.AddClaim(new Claim(ClaimTypes.Role, role.GetString()!));
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
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ParcelFlow Shipment API", Version = "v1" });
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
builder.Services.AddOpenTelemetry().ConfigureResource(resource => resource.AddService("parcelflow-shipment"))
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
        await app.Services.InitializeShipmentDatabaseAsync();
    }
}
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapPrometheusScrapingEndpoint().AllowAnonymous();
app.MapShipmentEndpoints();
app.Run();

public partial class Program;
