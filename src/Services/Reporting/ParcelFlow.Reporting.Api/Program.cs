using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using MassTransit.Logging;
using MassTransit.Monitoring;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ParcelFlow.BuildingBlocks.Api.Errors;
using ParcelFlow.Reporting.Api;
using ParcelFlow.Reporting.Application;
using ParcelFlow.Reporting.Infrastructure;
using ParcelFlow.Reporting.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
const long maximumRequestBytes = 1024 * 1024;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maximumRequestBytes);
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext().WriteTo.Console());
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddReportingApplication();
builder.Services.AddReportingInfrastructure(builder.Configuration);
builder.Services.AddExceptionHandler<ReportingExceptionHandler>();
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
            var subject = context.Principal?.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(subject) || subject.Length > 200 || subject != subject.Trim() || subject.Any(char.IsControl))
            {
                context.Fail("Missing subject claim.");
                return Task.CompletedTask;
            }
            if (context.Principal?.Identity is ClaimsIdentity identity)
            {
                foreach (var claim in identity.FindAll(ClaimTypes.Role).ToArray()) identity.RemoveClaim(claim);
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
    options.AddPolicy("reporting-users", policy => policy.RequireAuthenticatedUser().RequireRole("admin", "operator"));
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
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ParcelFlow Reporting API", Version = "v1" });
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
// Keep the shared JSON error writer first after versioning registers its writers.
builder.Services.AddParcelFlowProblemDetails();
var telemetryEndpoint = builder.Configuration["OpenTelemetry:Endpoint"];
builder.Services.AddOpenTelemetry().ConfigureResource(resource => resource.AddService("parcelflow-reporting"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddSource(DiagnosticHeaders.DefaultListenerName);
        if (!string.IsNullOrWhiteSpace(telemetryEndpoint))
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(telemetryEndpoint));
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation()
            .AddMeter(InstrumentationOptions.MeterName).AddPrometheusExporter();
        if (!string.IsNullOrWhiteSpace(telemetryEndpoint))
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(telemetryEndpoint));
    });

var app = builder.Build();
app.UseParcelFlowProblemDetails();
app.UseParcelFlowNoStore();
app.Use(async (context, next) =>
{
    if (context.Request.ContentLength > maximumRequestBytes)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }
    await next(context);
});
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    if (app.Configuration.GetValue<bool>("Database:Initialize"))
    {
        await app.Services.InitializeReportingDatabaseAsync(app.Configuration.GetValue<bool>("Database:SeedDemoData"));
    }
}
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapPrometheusScrapingEndpoint().AllowAnonymous();
app.MapReportingEndpoints();
app.Run();

public partial class Program;
