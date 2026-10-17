using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace ParcelFlow.BuildingBlocks.Api.Errors;

internal sealed class ParcelFlowProblemDetailsWriter(IOptions<ProblemDetailsOptions> options) : IProblemDetailsWriter
{
    // Errors always have a JSON representation, including when Accept excludes JSON.
    public bool CanWrite(ProblemDetailsContext context) => true;

    public async ValueTask WriteAsync(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var response = context.HttpContext.Response;
        problem.Status ??= response.StatusCode;
        problem.Title ??= ReasonPhrases.GetReasonPhrase(problem.Status.Value);
        problem.Type ??= $"https://httpstatuses.io/{problem.Status.Value}";
        problem.Instance ??= context.HttpContext.Request.Path;
        response.StatusCode = problem.Status.Value;
        options.Value.CustomizeProblemDetails?.Invoke(context);

        // Preserve the validation subtype even when MVC was registered first.
        if (problem is HttpValidationProblemDetails validation)
        {
            await response.WriteAsJsonAsync(validation, options: null,
                contentType: "application/problem+json", cancellationToken: context.HttpContext.RequestAborted);
        }
        else
        {
            await response.WriteAsJsonAsync(problem, options: null,
                contentType: "application/problem+json", cancellationToken: context.HttpContext.RequestAborted);
        }
    }
}
