using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ParcelFlow.BuildingBlocks.Api.Errors;
using Polly.CircuitBreaker;
using Xunit;

namespace ParcelFlow.BuildingBlocks.Tests;

public sealed class ApiExceptionMappingTests
{
    [Fact]
    public async Task Domain_argument_errors_return_400_without_exposing_the_exception_message()
    {
        var (context, problem) = await HandleAsync(
            new ArgumentException("keycloakSubject must contain between 1 and 200 characters.", "keycloakSubject"));

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var validation = problem.Should().BeOfType<HttpValidationProblemDetails>().Subject;
        validation.Status.Should().Be(StatusCodes.Status400BadRequest);
        validation.Title.Should().Be("Validation failed");
        validation.Detail.Should().Be("One or more request values are invalid.");
        validation.Errors.Keys.Should().Equal("keycloakSubject");
        validation.Errors["keycloakSubject"].Should().Equal(ApiExceptionMapping.InvalidValueMessage);
        JsonSerializer.Serialize(validation).Should().NotContain("between 1 and 200");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("value with spaces")]
    [InlineData("Npgsql.Connection")]
    public async Task Argument_errors_only_expose_well_formed_parameter_names(string? parameterName)
    {
        var (_, problem) = await HandleAsync(new ArgumentException("Internal detail", parameterName));

        problem.Should().BeOfType<HttpValidationProblemDetails>().Which.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Argument_out_of_range_errors_are_client_errors()
    {
        var (context, problem) = await HandleAsync(new ArgumentOutOfRangeException("weightKg", 42m, "Too heavy"));

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.Should().BeOfType<HttpValidationProblemDetails>().Which.Errors.Keys.Should().Equal("weightKg");
    }

    [Fact]
    public async Task Open_circuits_return_503_with_the_remaining_break_time_as_retry_after()
    {
        var (context, problem) = await HandleAsync(
            new BrokenCircuitException("The circuit is now open", TimeSpan.FromSeconds(2.2)));

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().Should().Be("3");
        problem.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        problem.Title.Should().Be("Service temporarily unavailable");
        problem.Detail.Should().BeNull();
    }

    [Fact]
    public async Task Open_circuits_without_break_time_use_the_default_retry_after()
    {
        var (context, _) = await HandleAsync(new IsolatedCircuitException("isolated"));

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().Should().Be("30");
    }

    [Fact]
    public async Task Wrapped_open_circuits_are_still_unavailable_errors()
    {
        var (context, _) = await HandleAsync(new InvalidOperationException("outer",
            new BrokenCircuitException("open", TimeSpan.FromMilliseconds(10))));

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().Should().Be("1");
    }

    public static TheoryData<Exception> CircuitWrappers
    {
        get
        {
            static BrokenCircuitException Open() => new("open", TimeSpan.FromSeconds(4));
            // FluentValidation's ValidationException exposes no inner-exception constructor, so it cannot wrap a circuit.
            return new TheoryData<Exception>
            {
                new ArgumentException("bad value", "subject", Open()),
                new ArgumentException("outer", new InvalidOperationException("middle", Open())),
                new JsonException("json", Open()),
                new BadHttpRequestException("bad request", Open()),
                new BadHttpRequestException("too large", StatusCodes.Status413PayloadTooLarge, Open())
            };
        }
    }

    [Theory]
    [MemberData(nameof(CircuitWrappers))]
    public async Task Open_circuits_wrapped_in_client_error_exceptions_still_return_503(Exception exception)
    {
        var (context, problem) = await HandleAsync(exception);

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().Should().Be("4");
        problem.Should().NotBeOfType<HttpValidationProblemDetails>();
        problem.Title.Should().Be("Service temporarily unavailable");
    }

    [Fact]
    public async Task Malformed_json_returns_400()
    {
        var (context, problem) = await HandleAsync(new JsonException("'x' is an invalid start of a value."));

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.Title.Should().Be("Invalid request");
        problem.Should().NotBeOfType<HttpValidationProblemDetails>();
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status413PayloadTooLarge, StatusCodes.Status413PayloadTooLarge)]
    [InlineData(StatusCodes.Status431RequestHeaderFieldsTooLarge, StatusCodes.Status400BadRequest)]
    public async Task Bad_http_requests_keep_413_and_otherwise_return_400(int thrownStatus, int expectedStatus)
    {
        var (context, problem) = await HandleAsync(new BadHttpRequestException("bad", thrownStatus));

        context.Response.StatusCode.Should().Be(expectedStatus);
        problem.Status.Should().Be(expectedStatus);
    }

    [Fact]
    public void Unexpected_errors_are_not_mapped()
    {
        ApiExceptionMapping.TryMap(new DefaultHttpContext(), new InvalidOperationException()).Should().BeNull();
    }

    [Theory]
    [InlineData(null, 30)]
    [InlineData(0d, 30)]
    [InlineData(-5d, 30)]
    [InlineData(0.001d, 1)]
    [InlineData(14.2d, 15)]
    public void Retry_after_is_rounded_up_to_whole_seconds(double? seconds, long expected)
    {
        ApiExceptionMapping.RetryAfterSeconds(seconds is { } value ? TimeSpan.FromSeconds(value) : null)
            .Should().Be(expected);
    }

    private static async Task<(DefaultHttpContext Context, Microsoft.AspNetCore.Mvc.ProblemDetails Problem)> HandleAsync(
        Exception exception)
    {
        var writer = new CapturingProblemDetailsService();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/customers";
        var handled = await new ApiExceptionHandler(writer, NullLogger<ApiExceptionHandler>.Instance)
            .TryHandleAsync(context, exception, CancellationToken.None);
        handled.Should().BeTrue();
        writer.Context!.ProblemDetails.Instance.Should().Be("/api/v1/customers");
        writer.Context.ProblemDetails.Extensions.Should().ContainKey("traceId");
        return (context, writer.Context.ProblemDetails);
    }

    private sealed class CapturingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? Context { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Context = context;
            return ValueTask.CompletedTask;
        }
    }
}
