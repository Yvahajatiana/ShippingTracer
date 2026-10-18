using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using ParcelFlow.Notification.Application;
using ParcelFlow.Notification.Domain;
using Polly;

namespace ParcelFlow.Notification.Infrastructure.Recipients;

public sealed class ShipmentRecipientResolver(IHttpClientFactory clients, IConfiguration configuration,
    IDistributedCache cache, ShipmentAccessToken accessToken) : IShipmentRecipientResolver
{
    private readonly ResiliencePipeline _pipeline = ShipmentHttpResilience.Create();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string CacheKey(Guid shipmentId) => "notification:recipient:v1:" + shipmentId.ToString("D");
    public async Task<ShipmentRecipient> ResolveAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        if (shipmentId == Guid.Empty) throw new ArgumentException("A shipment identifier is required.", nameof(shipmentId));
        var cached = await ReadCacheAsync(shipmentId, cancellationToken);
        if (cached is not null) return cached;
        for (var authenticationAttempt = 0; authenticationAttempt < 2; authenticationAttempt++)
        {
            var token = await accessToken.GetAsync(cancellationToken);
            var url = ShipmentHttpResilience.BaseUrl(configuration["ShipmentService:BaseUrl"], "ShipmentService:BaseUrl");
            using var response = await _pipeline.ExecuteAsync(async ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, $"api/v1/shipments/{shipmentId:D}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var received = await ShipmentHttpResilience.SendAsync(clients.CreateClient("notification-shipment"), request, ct);
                if (!received.IsSuccessStatusCode && received.StatusCode != HttpStatusCode.Unauthorized)
                {
                    var status = received.StatusCode; received.Dispose();
                    throw new HttpRequestException("Shipment contact resolution failed.", null, status);
                }
                return received;
            }, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await accessToken.InvalidateAsync(token, cancellationToken);
                if (authenticationAttempt == 0) continue;
                throw new HttpRequestException("Shipment contact resolution failed.", null, HttpStatusCode.Unauthorized);
            }
            ShipmentPayload? payload;
            try { payload = await response.Content.ReadFromJsonAsync<ShipmentPayload>(Json, cancellationToken); }
            catch (JsonException) { throw new InvalidOperationException("Shipment contact resolution returned an invalid response."); }
            var recipient = payload?.Sender is { } sender ? new ShipmentRecipient(payload.OwnerSubject, payload.TrackingNumber, sender.Email, sender.Phone) : null;
            if (!Valid(recipient)) throw new InvalidOperationException("Shipment contact resolution returned invalid contact details.");
            await WriteCacheAsync(shipmentId, recipient!, cancellationToken);
            return recipient!;
        }
        throw new InvalidOperationException("Shipment contact resolution failed.");
    }
    private static bool Valid(ShipmentRecipient? value) => value is not null && NotificationRules.IsSubject(value.CustomerSubject) &&
        NotificationRules.IsTrackingNumber(value.TrackingNumber) && NotificationRules.IsEmail(value.Email) && NotificationRules.IsPhone(value.Phone);
    private async Task<ShipmentRecipient?> ReadCacheAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
        try
        {
            var bytes = await cache.GetAsync(CacheKey(shipmentId), timeout.Token).WaitAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
            if (bytes is null || bytes.Length > 4096) return null;
            var value = JsonSerializer.Deserialize<ShipmentRecipient>(bytes, Json);
            return Valid(value) ? value : null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return null; }
    }
    private async Task WriteCacheAsync(Guid shipmentId, ShipmentRecipient result, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
        try { await cache.SetAsync(CacheKey(shipmentId), JsonSerializer.SerializeToUtf8Bytes(result, Json), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }, timeout.Token).WaitAsync(TimeSpan.FromMilliseconds(250), cancellationToken); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
    }
    private sealed record SenderPayload(string Email, string Phone);
    private sealed record ShipmentPayload(string OwnerSubject, string TrackingNumber, SenderPayload? Sender);
}
