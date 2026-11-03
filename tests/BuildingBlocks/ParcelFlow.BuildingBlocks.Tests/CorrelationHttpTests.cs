using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using ParcelFlow.BuildingBlocks.Api.Correlation;
using ParcelFlow.BuildingBlocks.Api.Errors;
using ParcelFlow.BuildingBlocks.Application.Correlation;
using Xunit;

namespace ParcelFlow.BuildingBlocks.Tests;

public sealed class CorrelationHttpTests
{
    [Fact]
    public async Task A_valid_incoming_correlation_becomes_ambient_and_is_echoed()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        var incoming = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ambient");
        request.Headers.Add(CorrelationExtensions.HeaderName, incoming.ToString());

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(incoming.ToString("D"));
        response.Headers.GetValues(CorrelationExtensions.HeaderName).Should().Equal(incoming.ToString("D"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task A_missing_or_invalid_correlation_is_replaced_by_a_new_one(string? header)
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ambient");
        if (header is not null) request.Headers.TryAddWithoutValidation(CorrelationExtensions.HeaderName, header);

        using var response = await client.SendAsync(request);

        var ambient = Guid.Parse(await response.Content.ReadAsStringAsync());
        ambient.Should().NotBeEmpty();
        response.Headers.GetValues(CorrelationExtensions.HeaderName).Should().Equal(ambient.ToString("D"));
    }

    [Fact]
    public async Task Error_responses_rewritten_by_the_exception_handler_keep_the_correlation()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        var incoming = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/crash");
        request.Headers.Add(CorrelationExtensions.HeaderName, incoming.ToString());

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Headers.GetValues(CorrelationExtensions.HeaderName).Should().Equal(incoming.ToString("D"));
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddParcelFlowProblemDetails();
        builder.Services.AddParcelFlowCorrelation();
        var app = builder.Build();
        app.UseParcelFlowCorrelation();
        app.UseParcelFlowProblemDetails();
        app.MapGet("/ambient", (ICorrelationContext correlation) => Results.Text(correlation.CorrelationId.ToString("D")));
        app.MapGet("/crash", IResult () => throw new InvalidOperationException("crash"));
        await app.StartAsync();
        return app;
    }
}
