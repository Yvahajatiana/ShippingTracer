using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using ParcelFlow.Tracking.Application;
using ParcelFlow.Tracking.Domain;

namespace ParcelFlow.Tracking.Api;

public static class TrackingEndpoints
{
    private static readonly JsonSerializerOptions CacheJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static void MapTrackingEndpoints(this WebApplication app)
    {
        var versions = app.NewApiVersionSet().HasApiVersion(new ApiVersion(1, 0)).ReportApiVersions().Build();
        var tracking = app.MapGroup("/api/v{version:apiVersion}/tracking").WithApiVersionSet(versions)
            .MapToApiVersion(1, 0).WithTags("Tracking");
        tracking.MapGet("/{trackingNumber}", async (string trackingNumber, HttpContext context,
            ISender sender, IDistributedCache cache, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var query = new GetPublicTracking(TrackingRules.NormalizeTrackingNumber(trackingNumber));
            // Validate before consulting Redis, including for malformed or hostile input.
            var validator = context.RequestServices.GetRequiredService<FluentValidation.IValidator<GetPublicTracking>>();
            var validation = await validator.ValidateAsync(query, ct);
            if (!validation.IsValid) throw new FluentValidation.ValidationException(validation.Errors);
            var key = $"tracking:public:v1:{query.TrackingNumber}";
            var logger = loggerFactory.CreateLogger("ParcelFlow.Tracking.PublicCache");
            var cached = await ReadCacheAsync(cache, key, logger, ct);
            if (cached is not null && cached.TrackingNumber == query.TrackingNumber)
                return TypedResults.Ok(cached);
            var result = await sender.Send(query, ct);
            await WriteCacheAsync(cache, key, result, logger, ct);
            return TypedResults.Ok(result);
        }).AllowAnonymous();
        tracking.MapGet("/shipments/{shipmentId:guid}", async (Guid shipmentId, HttpContext context,
            ISender sender, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(await sender.Send(new GetShipmentTracking(shipmentId), ct));
        }).RequireAuthorization("operations");
    }

    private static async Task<PublicTrackingDto?> ReadCacheAsync(IDistributedCache cache, string key,
        ILogger logger, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMilliseconds(250));
        try
        {
            var bytes = await cache.GetAsync(key, budget.Token).WaitAsync(budget.Token);
            return bytes is null ? null : JsonSerializer.Deserialize<PublicTrackingDto>(bytes, CacheJson);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Tracking cache read unavailable");
            return null;
        }
    }

    private static async Task WriteCacheAsync(IDistributedCache cache, string key, PublicTrackingDto value,
        ILogger logger, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMilliseconds(250));
        try
        {
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, CacheJson),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(15) },
                budget.Token).WaitAsync(budget.Token);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Tracking cache write unavailable");
        }
    }
}
