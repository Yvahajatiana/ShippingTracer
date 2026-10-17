using System.Diagnostics;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ParcelFlow.BuildingBlocks.Api.Errors;
using Xunit;

namespace ParcelFlow.BuildingBlocks.Tests;

public sealed class ApiExceptionHandlerTests
{
    [Fact]
    public async Task Validation_errors_are_grouped_by_property_and_deduplicated()
    {
        var writer = new CapturingProblemDetailsService();
        var handler = CreateHandler(writer);
        var context = CreateContext();
        var exception = new ValidationException(new[]
        {
            new ValidationFailure("Email", "Email is required"),
            new ValidationFailure("Email", "Email is required"),
            new ValidationFailure("Email", "Email is invalid"),
            new ValidationFailure("Name", "Name is required")
        });

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problem = writer.Context!.ProblemDetails.Should().BeOfType<HttpValidationProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Instance.Should().Be("/api/v1/customers");
        problem.Errors.Should().HaveCount(2);
        problem.Errors["Email"].Should().Equal("Email is required", "Email is invalid");
        problem.Errors["Name"].Should().Equal("Name is required");
    }

    [Fact]
    public async Task Unexpected_errors_return_500_without_exposing_exception_details()
    {
        var writer = new CapturingProblemDetailsService();
        var handler = CreateHandler(writer);
        var context = CreateContext();
        var exception = new InvalidOperationException("Database password is secret");

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var problem = writer.Context!.ProblemDetails;
        problem.Status.Should().Be(StatusCodes.Status500InternalServerError);
        problem.Title.Should().Be("An unexpected error occurred");
        problem.Detail.Should().BeNull();
        problem.Instance.Should().Be("/api/v1/customers");
        problem.Extensions.Keys.Should().Equal("traceId");
        writer.Context.Exception.Should().BeNull();
    }

    [Fact]
    public async Task Problem_details_include_the_current_activity_trace_identifier()
    {
        using var activity = new Activity("ApiExceptionHandlerTests").Start();
        var writer = new CapturingProblemDetailsService();
        var context = CreateContext();

        await CreateHandler(writer).TryHandleAsync(context, new Exception(), CancellationToken.None);

        writer.Context!.ProblemDetails.Extensions["traceId"].Should().Be(activity.Id);
    }

    [Fact]
    public async Task Problem_details_fall_back_to_the_http_trace_identifier()
    {
        var previousActivity = Activity.Current;
        try
        {
            Activity.Current = null;
            var writer = new CapturingProblemDetailsService();
            var context = CreateContext();
            context.TraceIdentifier = "request-trace-123";

            await CreateHandler(writer).TryHandleAsync(context, new Exception(), CancellationToken.None);

            writer.Context!.ProblemDetails.Extensions["traceId"].Should().Be("request-trace-123");
        }
        finally
        {
            Activity.Current = previousActivity;
        }
    }

    [Fact]
    public async Task Aborted_requests_are_not_handled_or_written()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var writer = new CapturingProblemDetailsService();
        var context = CreateContext();
        context.RequestAborted = cancellation.Token;

        var handled = await CreateHandler(writer).TryHandleAsync(
            context, new OperationCanceledException(cancellation.Token), cancellation.Token);

        handled.Should().BeFalse();
        writer.Context.Should().BeNull();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Cancellation_without_an_aborted_request_is_an_unexpected_error()
    {
        var writer = new CapturingProblemDetailsService();
        var context = CreateContext();

        var handled = await CreateHandler(writer).TryHandleAsync(
            context, new OperationCanceledException(), CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        writer.Context.Should().NotBeNull();
    }

    private static ApiExceptionHandler CreateHandler(CapturingProblemDetailsService writer) =>
        new(writer, NullLogger<ApiExceptionHandler>.Instance);

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/customers";
        return context;
    }

    private sealed class CapturingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? Context { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Context = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Context = context;
            return ValueTask.FromResult(true);
        }
    }
}
