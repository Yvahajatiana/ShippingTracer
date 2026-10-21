namespace ParcelFlow.Gateway;

public sealed class GatewayOptions
{
    public Dictionary<string, string> Destinations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public long MaximumRequestBodyBytes { get; set; } = 12 * 1024 * 1024;
    public int InactivityTimeoutSeconds { get; set; } = 30;
    public int PublicPermitLimit { get; set; } = 60;
    public int PrivatePermitLimit { get; set; } = 300;
    public int IpPermitLimit { get; set; } = 1000;
    public int RateLimitWindowSeconds { get; set; } = 60;
    public string[] CorsOrigins { get; set; } = [];

    public void Validate()
    {
        foreach (var service in GatewayRoutes.Services)
        {
            if (!Destinations.TryGetValue(service.Key, out var destination) ||
                !Uri.TryCreate(destination, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") ||
                uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException($"Gateway destination {service.Key} must be an HTTP(S) origin.");
        }
        if (MaximumRequestBodyBytes is <= 0 or > 1024L * 1024 * 1024 ||
            InactivityTimeoutSeconds is < 1 or > 600 || PublicPermitLimit < 1 ||
            PrivatePermitLimit < 1 || IpPermitLimit < 1 || RateLimitWindowSeconds is < 1 or > 3600)
            throw new InvalidOperationException("Gateway limits are invalid.");
        if (CorsOrigins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") || uri.GetLeftPart(UriPartial.Authority) != origin ||
            !string.IsNullOrEmpty(uri.UserInfo) || origin.Contains('*')))
            throw new InvalidOperationException("Gateway CORS origins must be explicit HTTP(S) origins without a trailing slash.");
    }
}

public static class GatewayRoutes
{
    public static IReadOnlyDictionary<string, string> Services { get; } = new Dictionary<string, string>
    {
        ["Customer"] = "customer-api", ["Shipment"] = "shipment-api", ["Tracking"] = "tracking-api",
        ["Warehouse"] = "warehouse-api", ["Delivery"] = "delivery-api", ["Billing"] = "billing-api",
        ["Notification"] = "notification-api", ["Reporting"] = "reporting-api"
    };
}
