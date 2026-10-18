using System.Net;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace ParcelFlow.Notification.Infrastructure.Recipients;

internal static class ShipmentHttpResilience
{
    public static ResiliencePipeline Create() => new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(IsTransient).Handle<TimeoutException>(),
            MaxRetryAttempts = 2, Delay = TimeSpan.FromMilliseconds(100), BackoffType = DelayBackoffType.Exponential, UseJitter = true
        })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(IsTransient).Handle<TimeoutException>(),
            FailureRatio = 0.5, MinimumThroughput = 6, SamplingDuration = TimeSpan.FromSeconds(30), BreakDuration = TimeSpan.FromSeconds(15)
        }).Build();
    private static bool IsTransient(HttpRequestException error) => error.StatusCode is null or HttpStatusCode.TooManyRequests || (int)error.StatusCode >= 500;
    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try { return await client.SendAsync(request, deadline.Token).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new TimeoutException("The shipment dependency timed out."); }
        catch (HttpRequestException error) { throw new HttpRequestException("The shipment dependency is unavailable.", null, error.StatusCode); }
    }
    public static Uri BaseUrl(string? value, string setting, bool allowHttp)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(allowHttp && uri.Scheme == "http")) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException($"{setting} must be an absolute HTTPS URL without credentials, query or fragment; HTTP is allowed only in Development.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
}
