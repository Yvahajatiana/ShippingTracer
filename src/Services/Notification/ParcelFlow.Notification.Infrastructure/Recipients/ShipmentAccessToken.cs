using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Polly;

namespace ParcelFlow.Notification.Infrastructure.Recipients;

public sealed class ShipmentAccessToken(IHttpClientFactory clients, IConfiguration configuration, IHostEnvironment environment) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ResiliencePipeline _pipeline = ShipmentHttpResilience.Create();
    private string? _token;
    private DateTimeOffset _expires;
    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && _expires > DateTimeOffset.UtcNow) return _token;
            var clientId = configuration["ServiceAuthentication:ClientId"];
            var secret = configuration["ServiceAuthentication:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("Service authentication credentials are required.");
            var authority = ShipmentHttpResilience.BaseUrl(configuration["Keycloak:Authority"], "Keycloak:Authority", environment.IsDevelopment());
            var payload = await _pipeline.ExecuteAsync(async ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(authority, "protocol/openid-connect/token"));
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = secret });
                using var response = await ShipmentHttpResilience.SendAsync(clients.CreateClient("notification-keycloak"), request, ct);
                if (!response.IsSuccessStatusCode) throw new HttpRequestException("Service authentication failed.", null, response.StatusCode);
                try { return await response.Content.ReadFromJsonAsync<TokenPayload>(cancellationToken: ct); }
                catch (System.Text.Json.JsonException) { throw new InvalidOperationException("Service authentication returned an invalid response."); }
            }, cancellationToken);
            if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken) || payload.AccessToken.Length > 16384 || payload.AccessToken.Any(char.IsControl) || payload.ExpiresIn is <= 0 or > 86400 || !string.Equals(payload.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Service authentication returned an invalid token.");
            _token = payload.AccessToken;
            _expires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, payload.ExpiresIn - 30));
            return _token;
        }
        finally { _gate.Release(); }
    }
    public async Task InvalidateAsync(string rejectedToken, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { if (_token == rejectedToken) { _token = null; _expires = default; } }
        finally { _gate.Release(); }
    }
    public void Dispose() => _gate.Dispose();
    private sealed record TokenPayload([property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn, [property: JsonPropertyName("token_type")] string TokenType);
}
