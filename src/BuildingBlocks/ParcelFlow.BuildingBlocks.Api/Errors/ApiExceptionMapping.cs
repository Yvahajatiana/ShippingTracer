using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Polly.CircuitBreaker;

namespace ParcelFlow.BuildingBlocks.Api.Errors;

/// <summary>
/// Maps the exceptions shared by every ParcelFlow API to client-safe ProblemDetails.
/// Service-specific handlers run first and only map their own business exceptions.
/// </summary>
public static partial class ApiExceptionMapping
{
    public const string InvalidValueMessage = "The value is invalid.";

    /// <summary>Retry-After used when an open circuit does not report its remaining break time.</summary>
    public static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Returns the mapped problem for a known client or availability error, or null for an unexpected error.
    /// Response headers required by the mapping, such as Retry-After, are set on <paramref name="context"/>.
    /// </summary>
    public static ProblemDetails? TryMap(HttpContext context, Exception exception)
    {
        switch (exception)
        {
            case ValidationException validation:
                return Validation(validation.Errors
                    .GroupBy(failure => failure.PropertyName)
                    .ToDictionary(group => group.Key,
                        group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray()));
            case ArgumentException argument:
                // Domain guards throw ArgumentException; their messages are not a client contract, so only
                // a generic message keyed by a well-formed parameter name is exposed.
                var errors = new Dictionary<string, string[]>();
                if (argument.ParamName is { } name && ParameterName().IsMatch(name))
                    errors[name] = [InvalidValueMessage];
                var problem = Validation(errors);
                problem.Detail = "One or more request values are invalid.";
                return problem;
            case BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }:
                return Simple(StatusCodes.Status413PayloadTooLarge, "Request too large");
            case JsonException or BadHttpRequestException:
                return Simple(StatusCodes.Status400BadRequest, "Invalid request");
        }

        if (FindBrokenCircuit(exception) is { } brokenCircuit)
        {
            context.Response.Headers.RetryAfter = RetryAfterSeconds(brokenCircuit.RetryAfter)
                .ToString(CultureInfo.InvariantCulture);
            return new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Service temporarily unavailable",
                Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.6.4"
            };
        }

        return null;
    }

    /// <summary>Retry-After in whole seconds, rounded up and at least one second.</summary>
    public static long RetryAfterSeconds(TimeSpan? retryAfter)
    {
        var value = retryAfter is { } remaining && remaining > TimeSpan.Zero ? remaining : DefaultRetryAfter;
        return Math.Max(1, (long)Math.Ceiling(value.TotalSeconds));
    }

    private static BrokenCircuitException? FindBrokenCircuit(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is BrokenCircuitException brokenCircuit) return brokenCircuit;
        }
        return null;
    }

    private static HttpValidationProblemDetails Validation(IDictionary<string, string[]> errors) => new(errors)
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "Validation failed",
        Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.1"
    };

    private static ProblemDetails Simple(int status, string title) => new()
    {
        Status = status,
        Title = title,
        Type = $"https://httpstatuses.io/{status}"
    };

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterName();
}
