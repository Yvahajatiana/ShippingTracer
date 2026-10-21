using System.Net;
using Yarp.ReverseProxy.Forwarder;

namespace ParcelFlow.Gateway;

internal static class GatewayForwarding
{
    private static readonly HttpTransformer Transformer = new TrustedTransformer();

    public static bool IsUntrustedHeader(string name) => name.Equals("Forwarded", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Real-IP", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Original-For", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Original-Proto", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Original-Host", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Original-URL", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Rewrite-URL", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Remote-User", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Authenticated-User", StringComparison.OrdinalIgnoreCase);

    public static async Task SendAsync(HttpContext context, IHttpForwarder forwarder, HttpMessageInvoker client,
        string destination, GatewayOptions options)
    {
        var error = await forwarder.SendAsync(context, destination, client, new ForwarderRequestConfig
        {
            ActivityTimeout = TimeSpan.FromSeconds(options.InactivityTimeoutSeconds)
        }, Transformer);
        if (error == ForwarderError.None || context.Response.HasStarted || context.RequestAborted.IsCancellationRequested) return;
        var status = context.Response.StatusCode;
        // Kestrel reports a streamed/chunked body exceeding its limit while YARP is reading it.
        for (var exception = context.Features.Get<IForwarderErrorFeature>()?.Exception;
             exception is not null; exception = exception.InnerException)
        {
            if (exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
                status = StatusCodes.Status413PayloadTooLarge;
        }
        context.Response.Clear();
        context.Response.StatusCode = status == StatusCodes.Status504GatewayTimeout
            ? StatusCodes.Status504GatewayTimeout
            : status == StatusCodes.Status413PayloadTooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status502BadGateway;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext { HttpContext = context });
    }

    private sealed class TrustedTransformer : HttpTransformer
    {
        public override async ValueTask TransformRequestAsync(HttpContext context, HttpRequestMessage request,
            string destinationPrefix, CancellationToken cancellationToken)
        {
            await base.TransformRequestAsync(context, request, destinationPrefix, cancellationToken);
            request.Headers.Host = null;
            foreach (var header in request.Headers.Select(header => header.Key).Where(IsUntrustedHeader).ToArray())
                request.Headers.Remove(header);
            if (context.Connection.RemoteIpAddress is { } address)
                request.Headers.TryAddWithoutValidation("X-Forwarded-For", address.ToString());
            request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", context.Request.Scheme);
            request.Headers.TryAddWithoutValidation("X-Forwarded-Host", context.Request.Host.Value);
        }
    }
}
