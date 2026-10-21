using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Cors.Infrastructure;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ParcelFlow.BuildingBlocks.Api.Errors;
using ParcelFlow.Gateway;
using Serilog;
using Yarp.ReverseProxy.Forwarder;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel((context, options) => options.Limits.MaxRequestBodySize =
    context.Configuration.GetSection("Gateway").Get<GatewayOptions>()!.MaximumRequestBodyBytes);
builder.Host.UseSerilog((context, logging) => logging.ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext().WriteTo.Console());
builder.Services.AddSingleton(services =>
{
    var options = services.GetRequiredService<IConfiguration>().GetSection("Gateway").Get<GatewayOptions>() ?? new GatewayOptions();
    options.Validate();
    return options;
});
builder.Services.AddHttpForwarder();
builder.Services.AddSingleton(services => new HttpMessageInvoker(new SocketsHttpHandler
{
    UseProxy = false, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None,
    UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(services.GetRequiredService<GatewayOptions>().InactivityTimeoutSeconds)
}));
builder.Services.AddParcelFlowProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration, IHostEnvironment>((options, configuration, environment) =>
{
    var authority = configuration["Keycloak:Authority"];
    if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) ||
        (authorityUri.Scheme != "https" && (!environment.IsDevelopment() || authorityUri.Scheme != "http")))
        throw new InvalidOperationException("Keycloak authority must use HTTPS outside Development.");
    options.Authority = authority;
    options.RequireHttpsMetadata = !environment.IsDevelopment();
    options.MapInboundClaims = false;
    options.TokenValidationParameters.ValidAudiences = GatewayRoutes.Services.Values;
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            var subjects = context.Principal?.FindAll("sub").Select(claim => claim.Value).ToArray() ?? [];
            if (subjects.Length != 1 || string.IsNullOrWhiteSpace(subjects[0]) || subjects[0].Length > 200 ||
                subjects[0] != subjects[0].Trim() || subjects[0].Any(char.IsControl))
                context.Fail("Invalid subject claim.");
            return Task.CompletedTask;
        }
    };
}).ValidateOnStart();
builder.Services.AddAuthorization(options =>
{
    foreach (var audience in GatewayRoutes.Services.Values)
        options.AddPolicy($"audience:{audience}", policy => policy.RequireAuthenticatedUser().RequireClaim("aud", audience));
});
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<GatewayOptions>((options, gateway) => options.AddDefaultPolicy(policy =>
{
    if (gateway.CorsOrigins.Length > 0)
        policy.WithOrigins(gateway.CorsOrigins).AllowAnyMethod().AllowAnyHeader()
            .WithExposedHeaders("Location", "Content-Disposition", "Retry-After");
}));
builder.Services.AddRateLimiter(_ => { });
builder.Services.AddOptions<RateLimiterOptions>().Configure<GatewayOptions>((options, gateway) =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/api")
            ? RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => Window(gateway.IpPermitLimit))
            : RateLimitPartition.GetNoLimiter("operational"));
    options.AddPolicy("public", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => Window(gateway.PublicPermitLimit)));
    options.AddPolicy("private", context => context.User.Identity?.IsAuthenticated == true
        ? RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirst("sub")!.Value, _ => Window(gateway.PrivatePermitLimit))
        : RateLimitPartition.GetNoLimiter("anonymous"));
    options.OnRejected = async (context, _) =>
    {
        var retry = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var remaining)
            ? Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds)) : gateway.RateLimitWindowSeconds;
        context.HttpContext.Response.Headers.RetryAfter = retry.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(
            new ProblemDetailsContext { HttpContext = context.HttpContext });
    };
    FixedWindowRateLimiterOptions Window(int limit) => new()
    {
        PermitLimit = limit, Window = TimeSpan.FromSeconds(gateway.RateLimitWindowSeconds),
        QueueLimit = 0, AutoReplenishment = true
    };
});
var telemetryEndpoint = builder.Configuration["OpenTelemetry:Endpoint"];
builder.Services.AddOpenTelemetry().ConfigureResource(resource => resource.AddService("parcelflow-gateway"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(telemetryEndpoint)) tracing.AddOtlpExporter(options => options.Endpoint = new Uri(telemetryEndpoint));
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddPrometheusExporter();
        if (!string.IsNullOrWhiteSpace(telemetryEndpoint)) metrics.AddOtlpExporter(options => options.Endpoint = new Uri(telemetryEndpoint));
    });
var app = builder.Build();
var gateway = app.Services.GetRequiredService<GatewayOptions>();
app.UseParcelFlowProblemDetails();
app.Use(async (context, next) =>
{
    foreach (var header in context.Request.Headers.Keys.Where(GatewayForwarding.IsUntrustedHeader).ToArray())
        context.Request.Headers.Remove(header);
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.OnStarting(() => { context.Response.Headers.CacheControl = "no-store"; return Task.CompletedTask; });
    }
    if (context.Request.ContentLength > gateway.MaximumRequestBodyBytes)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }
    await next(context);
});
app.UseSerilogRequestLogging();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
// Readiness means valid loaded configuration and a running gateway, independently of downstream availability.
app.MapHealthChecks("/health/ready");
app.MapPrometheusScrapingEndpoint();
Map("Customer", "/api/v1/customers/{**remainder}");
Map("Customer", "/api/v1/companies/{**remainder}");
Map("Shipment", "/api/v1/shipments/{**remainder}");
Map("Shipment", "/api/v1/tariffs/{**remainder}");
Map("Tracking", "/api/v1/tracking/{**remainder}");
Map("Warehouse", "/api/v1/warehouses/{**remainder}");
Map("Delivery", "/api/v1/delivery/{**remainder}");
Map("Billing", "/api/v1/billing/{**remainder}");
Map("Notification", "/api/v1/notifications/{**remainder}");
Map("Reporting", "/api/v1/reports/{**remainder}");
app.MapGet("/api/v1/tracking/{trackingNumber}", (HttpContext context, IHttpForwarder forwarder, HttpMessageInvoker client) =>
    GatewayForwarding.SendAsync(context, forwarder, client, gateway.Destinations["Tracking"], gateway))
    .AllowAnonymous().RequireRateLimiting("public");
app.Run();

void Map(string service, string pattern) => app.Map(pattern,
    (HttpContext context, IHttpForwarder forwarder, HttpMessageInvoker client) =>
        GatewayForwarding.SendAsync(context, forwarder, client, gateway.Destinations[service], gateway))
    .RequireAuthorization($"audience:{GatewayRoutes.Services[service]}").RequireRateLimiting("private");

public partial class Program;

