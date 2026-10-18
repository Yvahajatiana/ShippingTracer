using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.Notification.Infrastructure.Recipients;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using Xunit;

namespace ParcelFlow.Notification.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class RecipientResolverTests(InfrastructureFixture infrastructure)
{
    private const string Tracking = "PF800000000000000000000001";
    private static readonly string Snapshot = JsonSerializer.Serialize(new { ownerSubject = "owner", trackingNumber = Tracking, sender = new { email = "sender@example.test", phone = "+33600000001" } });
    private static HttpResponseMessage Json(string value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Token(int index = 1) => Json($"{{\"access_token\":\"service-token-{index}\",\"token_type\":\"Bearer\",\"expires_in\":300}}");
    private Harness Create(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle, IDistributedCache? cache = null, bool secret = true)
    {
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ShipmentService:BaseUrl"] = "http://shipment.test:5102/", ["Keycloak:Authority"] = "http://keycloak.test/realms/parcelflow",
            ["ServiceAuthentication:ClientId"] = "notification-service", ["ServiceAuthentication:ClientSecret"] = secret ? "test-secret-never-log" : null
        }).Build();
        var services = new ServiceCollection(); services.AddStackExchangeRedisCache(options => { options.Configuration = infrastructure.Redis.GetConnectionString(); options.InstanceName = "parcelflow:"; });
        var provider = services.BuildServiceProvider(); var handler = new Handler(handle); var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var clients = new Factory(client); var access = new ShipmentAccessToken(clients, settings);
        return new Harness(new ShipmentRecipientResolver(clients, settings, cache ?? provider.GetRequiredService<IDistributedCache>(), access), access, provider, client, provider.GetRequiredService<IDistributedCache>());
    }
    [Fact]
    public async Task Client_credentials_token_is_memory_cached_and_only_immutable_recipient_is_cached_in_real_Redis()
    {
        var tokens = 0; var shipments = 0; string? form = null; var authorizations = new List<string?>();
        using var harness = Create(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post) { tokens++; form = await request.Content!.ReadAsStringAsync(ct); return Token(); }
            shipments++; authorizations.Add(request.Headers.Authorization?.ToString()); return Json(Snapshot);
        });
        var id = Guid.NewGuid(); var first = await harness.Resolver.ResolveAsync(id, default); var second = await harness.Resolver.ResolveAsync(id, default);
        first.Should().Be(second); first.Email.Should().Be("sender@example.test"); tokens.Should().Be(1); shipments.Should().Be(1);
        form.Should().Contain("grant_type=client_credentials").And.Contain("client_id=notification-service").And.Contain("client_secret=test-secret-never-log");
        authorizations.Should().Equal("Bearer service-token-1");
        await harness.Resolver.ResolveAsync(Guid.NewGuid(), default); tokens.Should().Be(1); shipments.Should().Be(2);
        var bytes = await harness.Cache.GetAsync(ShipmentRecipientResolver.CacheKey(id)); bytes.Should().NotBeNull();
        var cached = Encoding.UTF8.GetString(bytes!); cached.Should().NotContain("service-token").And.NotContain("test-secret");
        using var document = JsonDocument.Parse(bytes!); document.RootElement.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo("customerSubject", "trackingNumber", "email", "phone");
        using var redis = await ConnectionMultiplexer.ConnectAsync(infrastructure.Redis.GetConnectionString());
        var ttl = await redis.GetDatabase().KeyTimeToLiveAsync("parcelflow:" + ShipmentRecipientResolver.CacheKey(id)); ttl.Should().NotBeNull(); ttl!.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(4)).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(5));
    }
    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"customerSubject\":\"owner\",\"trackingNumber\":\"bad\",\"email\":\"sender@example.test\",\"phone\":\"+33600000001\"}")]
    [InlineData("{\"customerSubject\":\"owner\",\"trackingNumber\":\"PF800000000000000000000001\",\"email\":\"Name <sender@example.test>\",\"phone\":\"+33600000001\"}")]
    [InlineData("{\"customerSubject\":\"owner\",\"trackingNumber\":\"PF800000000000000000000001\",\"email\":\"sender@example.test\",\"phone\":\"bad\\nphone\"}")]
    public async Task Invalid_or_corrupt_Redis_values_fall_back_to_authenticated_source(string corrupt)
    {
        var requests = 0; using var harness = Create((request, _) => { if (request.Method == HttpMethod.Post) return Task.FromResult(Token()); requests++; return Task.FromResult(Json(Snapshot)); });
        var id = Guid.NewGuid(); await harness.Cache.SetStringAsync(ShipmentRecipientResolver.CacheKey(id), corrupt);
        (await harness.Resolver.ResolveAsync(id, default)).TrackingNumber.Should().Be(Tracking); requests.Should().Be(1);
        (await harness.Cache.GetStringAsync(ShipmentRecipientResolver.CacheKey(id))).Should().Contain("sender@example.test");
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cache_failure_or_uncooperative_timeout_cannot_block_contact_resolution(bool timeout)
    {
        using var harness = Create((request, _) => Task.FromResult(request.Method == HttpMethod.Post ? Token() : Json(Snapshot)), new UnavailableCache(timeout));
        var clock = Stopwatch.StartNew(); (await harness.Resolver.ResolveAsync(Guid.NewGuid(), default)).Email.Should().Be("sender@example.test");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }
    [Fact]
    public async Task Unauthorized_source_refreshes_rejected_token_once_then_caches_success()
    {
        var tokens = 0; var gets = 0; var authorizations = new List<string?>();
        using var harness = Create((request, _) =>
        {
            if (request.Method == HttpMethod.Post) return Task.FromResult(Token(++tokens));
            authorizations.Add(request.Headers.Authorization?.ToString()); return Task.FromResult(++gets == 1 ? Json("{}", HttpStatusCode.Unauthorized) : Json(Snapshot));
        });
        await harness.Resolver.ResolveAsync(Guid.NewGuid(), default); tokens.Should().Be(2); gets.Should().Be(2);
        authorizations.Should().Equal("Bearer service-token-1", "Bearer service-token-2");
    }
    [Fact]
    public async Task Missing_secret_fails_before_any_dependency_request_without_exposing_credentials()
    {
        var requests = 0; using var harness = Create((_, _) => { requests++; return Task.FromResult(Token()); }, secret: false);
        var failure = await FluentActions.Awaiting(() => harness.Resolver.ResolveAsync(Guid.NewGuid(), default)).Should().ThrowAsync<InvalidOperationException>();
        failure.Which.ToString().Should().NotContain("test-secret"); requests.Should().Be(0);
    }
    [Fact]
    public async Task Missing_shipment_is_not_cached_or_retried_and_token_is_reused()
    {
        var tokens = 0; var gets = 0;
        using var harness = Create((request, _) => { if (request.Method == HttpMethod.Post) { tokens++; return Task.FromResult(Token()); } gets++; return Task.FromResult(Json("{}", HttpStatusCode.NotFound)); });
        var id = Guid.NewGuid();
        for (var attempt = 0; attempt < 2; attempt++) await FluentActions.Awaiting(() => harness.Resolver.ResolveAsync(id, default)).Should().ThrowAsync<HttpRequestException>().Where(x => x.StatusCode == HttpStatusCode.NotFound);
        tokens.Should().Be(1); gets.Should().Be(2); (await harness.Cache.GetAsync(ShipmentRecipientResolver.CacheKey(id))).Should().BeNull();
    }
    [Fact]
    public async Task Transient_http_failures_retry_then_open_shared_circuit_breaker()
    {
        var gets = 0; using var harness = Create((request, _) => { if (request.Method == HttpMethod.Post) return Task.FromResult(Token()); gets++; return Task.FromResult(Json("private upstream body", HttpStatusCode.ServiceUnavailable)); });
        for (var attempt = 0; attempt < 2; attempt++) await FluentActions.Awaiting(() => harness.Resolver.ResolveAsync(Guid.NewGuid(), default)).Should().ThrowAsync<HttpRequestException>();
        gets.Should().Be(6);
        await FluentActions.Awaiting(() => harness.Resolver.ResolveAsync(Guid.NewGuid(), default)).Should().ThrowAsync<BrokenCircuitException>(); gets.Should().Be(6);
    }
    [Theory]
    [InlineData("{\"ownerSubject\":\"owner\",\"trackingNumber\":\"bad\",\"sender\":{\"email\":\"sender@example.test\",\"phone\":\"+33600000001\"}}")]
    [InlineData("{\"ownerSubject\":\"owner\",\"trackingNumber\":\"PF800000000000000000000001\",\"sender\":null}")]
    [InlineData("private-not-json")]
    public async Task Invalid_source_payload_is_not_cached_and_exception_does_not_include_body(string body)
    {
        using var harness = Create((request, _) => Task.FromResult(request.Method == HttpMethod.Post ? Token() : Json(body))); var id = Guid.NewGuid();
        var failure = await FluentActions.Awaiting(() => harness.Resolver.ResolveAsync(id, default)).Should().ThrowAsync<InvalidOperationException>();
        failure.Which.ToString().Should().NotContain("sender@example.test").And.NotContain("private-not-json");
        (await harness.Cache.GetAsync(ShipmentRecipientResolver.CacheKey(id))).Should().BeNull();
    }
    [Fact]
    public async Task Caller_cancellation_stops_http_without_retry()
    {
        var requests = 0; using var harness = Create(async (_, ct) => { requests++; await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Token(); });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await FluentActions.Awaiting(() => harness.Resolver.ResolveAsync(Guid.NewGuid(), cancellation.Token)).Should().ThrowAsync<OperationCanceledException>(); requests.Should().Be(1);
    }
    [Fact]
    public async Task Uncooperative_http_dependency_is_bounded_to_five_seconds_per_attempt()
    {
        var gets = 0;
        using var harness = Create((request, _) =>
        {
            if (request.Method == HttpMethod.Post) return Task.FromResult(Token());
            Interlocked.Increment(ref gets); return new TaskCompletionSource<HttpResponseMessage>().Task;
        });
        var clock = Stopwatch.StartNew();
        await FluentActions.Awaiting(() => harness.Resolver.ResolveAsync(Guid.NewGuid(), default)).Should().ThrowAsync<TimeoutException>();
        gets.Should().Be(3); clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
    }
    [Fact]
    public async Task Concurrent_shipments_share_one_client_credentials_refresh()
    {
        var tokens = 0;
        using var harness = Create(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post) { Interlocked.Increment(ref tokens); await Task.Delay(50, ct); return Token(); }
            return Json(Snapshot);
        });
        var recipients = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => harness.Resolver.ResolveAsync(Guid.NewGuid(), default)));
        recipients.Should().HaveCount(10); tokens.Should().Be(1);
    }
    [Fact]
    public async Task Authentication_transient_failures_have_one_retry_budget_without_nested_shipment_retries()
    {
        var requests = 0;
        using var harness = Create((_, _) => { requests++; return Task.FromResult(Json("private auth error", HttpStatusCode.ServiceUnavailable)); });
        var failure = await FluentActions.Awaiting(() => harness.Resolver.ResolveAsync(Guid.NewGuid(), default)).Should().ThrowAsync<HttpRequestException>();
        requests.Should().Be(3); failure.Which.ToString().Should().NotContain("private auth error");
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed record Harness(ShipmentRecipientResolver Resolver, ShipmentAccessToken Access, ServiceProvider Provider, HttpClient Client, IDistributedCache Cache) : IDisposable
    {
        public void Dispose() { Access.Dispose(); Client.Dispose(); Provider.Dispose(); }
    }
    private sealed class UnavailableCache(bool timeout) : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("Unavailable");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => timeout ? new TaskCompletionSource<byte[]?>().Task : Task.FromException<byte[]?>(new InvalidOperationException("Unavailable"));
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new InvalidOperationException("Unavailable");
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => timeout ? new TaskCompletionSource().Task : Task.FromException(new InvalidOperationException("Unavailable"));
        public void Refresh(string key) => throw new InvalidOperationException("Unavailable");
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.FromException(new InvalidOperationException("Unavailable"));
        public void Remove(string key) => throw new InvalidOperationException("Unavailable");
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.FromException(new InvalidOperationException("Unavailable"));
    }
}
