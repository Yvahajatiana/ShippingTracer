using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using ParcelFlow.BuildingBlocks.Api.Correlation;
using ParcelFlow.BuildingBlocks.Api.Errors;
using ParcelFlow.BuildingBlocks.Api.Hosting;
using ParcelFlow.BuildingBlocks.Api.Operations;
using ParcelFlow.BuildingBlocks.Api.Observability;
using ParcelFlow.Delivery.Api;
using ParcelFlow.Delivery.Application;
using ParcelFlow.Delivery.Infrastructure;
using ParcelFlow.Delivery.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
const long maximumRequestBytes = 12 * 1024 * 1024;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maximumRequestBytes);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IDeliveryActor, HttpDeliveryActor>();
builder.AddParcelFlowObservability("parcelflow-delivery");
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddDeliveryApplication();
builder.Services.AddDeliveryInfrastructure(builder.Configuration);
builder.Services.AddExceptionHandler<DeliveryExceptionHandler>();
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
    options.AddPolicy("operations", policy => policy.RequireAuthenticatedUser().RequireRole("admin", "operator"));
    options.AddPolicy("admin", policy => policy.RequireAuthenticatedUser().RequireRole("admin"));
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
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ParcelFlow Delivery API", Version = "v1" });
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

var app = builder.Build();
app.UseParcelFlowCorrelation();
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
}
if (app.Environment.AllowsStartupDatabaseInitialization() && app.Configuration.GetValue<bool>("Database:Initialize"))
{
    await app.Services.InitializeDeliveryDatabaseAsync();
}
app.MapParcelFlowOperationalEndpoints().AllowAnonymous().MapPrometheusScrapingEndpoint();
app.MapDeliveryEndpoints();
app.Run();

public partial class Program;
