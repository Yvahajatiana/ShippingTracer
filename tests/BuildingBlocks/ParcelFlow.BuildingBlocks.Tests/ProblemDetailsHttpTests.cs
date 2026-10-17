using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.BuildingBlocks.Api.Errors;
using Xunit;

namespace ParcelFlow.BuildingBlocks.Tests;

public sealed class ProblemDetailsHttpTests
{
    public static TheoryData<bool, string, string, int> Cases
    {
        get
        {
            var cases = new TheoryData<bool, string, string, int>();
            foreach (var mvcFirst in new[] { true, false })
            foreach (var accept in new[] { "application/json", "application/xml" })
            {
                cases.Add(mvcFirst, accept, "validation", 400);
                cases.Add(mvcFirst, accept, "server", 500);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Errors_preserve_status_trace_and_validation_fields_regardless_of_accept_or_mvc_order(
        bool mvcFirst, string accept, string path, int status)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        if (mvcFirst) builder.Services.AddControllers().AddApplicationPart(typeof(ReviewRegressionController).Assembly);
        builder.Services.AddParcelFlowProblemDetails();
        if (!mvcFirst) builder.Services.AddControllers().AddApplicationPart(typeof(ReviewRegressionController).Assembly);
        await using var app = builder.Build();
        app.UseParcelFlowProblemDetails();
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        using var response = await client.GetAsync($"/review-regression/{path}");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var problem = document.RootElement;

        ((int)response.StatusCode).Should().Be(status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        problem.GetProperty("status").GetInt32().Should().Be(status);
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        if (status == 400)
            problem.GetProperty("errors").GetProperty("Email")[0].GetString().Should().Be("Email is required");
        else
            json.Should().NotContain("internal-server-secret");
    }
}

[ApiController]
[Route("review-regression")]
public sealed class ReviewRegressionController : ControllerBase
{
    [HttpGet("validation")]
    public IActionResult Validation() => throw new ValidationException(
        [new ValidationFailure("Email", "Email is required")]);

    [HttpGet("server")]
    public IActionResult Server() => throw new InvalidOperationException("internal-server-secret");
}
