using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.BuildingBlocks.Api.Errors;
using Polly.CircuitBreaker;
using Xunit;

namespace ParcelFlow.BuildingBlocks.Tests;

public sealed class NoStoreHttpTests
{
    [Theory]
    [InlineData("/api/ok", 200)]
    [InlineData("/api/missing", 404)]
    [InlineData("/api/forbidden", 403)]
    [InlineData("/api/argument", 400)]
    [InlineData("/api/circuit", 503)]
    [InlineData("/api/crash", 500)]
    [InlineData("/api/unknown-route", 404)]
    public async Task Api_responses_are_never_stored_including_errors_from_the_exception_handler(string path, int status)
    {
        await using var app = await StartAsync(useNoStore: true);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(path);

        ((int)response.StatusCode).Should().Be(status);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Theory]
    [InlineData("/public/missing", 404)]
    [InlineData("/public/argument", 400)]
    [InlineData("/public/crash", 500)]
    [InlineData("/public/forbidden", 403)]
    public async Task Problem_details_are_never_stored_even_without_the_no_store_middleware(string path, int status)
    {
        await using var app = await StartAsync(useNoStore: false);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(path);

        ((int)response.StatusCode).Should().Be(status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Successful_responses_outside_the_prefix_keep_their_cache_policy()
    {
        await using var app = await StartAsync(useNoStore: true);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/public/ok");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().BeNull();
    }

    [Fact]
    public async Task Open_circuit_response_carries_retry_after_and_a_generic_problem()
    {
        await using var app = await StartAsync(useNoStore: true);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/circuit");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(5));
        document.RootElement.GetProperty("status").GetInt32().Should().Be(503);
        document.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        json.Should().NotContain("circuit-secret");
    }

    [Fact]
    public async Task Service_specific_handlers_write_the_shared_problem_shape()
    {
        await using var app = await StartAsync(useNoStore: false);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/public/missing");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var problem = document.RootElement;

        problem.GetProperty("status").GetInt32().Should().Be(404);
        problem.GetProperty("title").GetString().Should().Be("Resource not found");
        problem.GetProperty("type").GetString().Should().Be("https://httpstatuses.io/404");
        problem.GetProperty("instance").GetString().Should().Be("/public/missing");
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static async Task<WebApplication> StartAsync(bool useNoStore)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddExceptionHandler<ServiceExceptionHandler>();
        builder.Services.AddParcelFlowProblemDetails();
        var app = builder.Build();
        app.UseParcelFlowProblemDetails();
        if (useNoStore) app.UseParcelFlowNoStore();
        foreach (var prefix in new[] { "/api", "/public" })
        {
            app.MapGet($"{prefix}/ok", () => Results.Ok(new { value = 1 }));
            app.MapGet($"{prefix}/missing", IResult () => throw new ResourceMissingException());
            app.MapGet($"{prefix}/forbidden", () => Results.StatusCode(StatusCodes.Status403Forbidden));
            app.MapGet($"{prefix}/argument", IResult () => throw new ArgumentException("secret detail", "subject"));
            app.MapGet($"{prefix}/circuit", IResult () =>
                throw new BrokenCircuitException("circuit-secret", TimeSpan.FromSeconds(5)));
            app.MapGet($"{prefix}/crash", IResult () => throw new InvalidOperationException("crash-secret"));
        }
        await app.StartAsync();
        return app;
    }

    private sealed class ResourceMissingException : Exception;

    private sealed class ServiceExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            if (exception is not ResourceMissingException) return false;
            await problemDetailsService.WriteProblemAsync(context, StatusCodes.Status404NotFound, "Resource not found");
            return true;
        }
    }
}
