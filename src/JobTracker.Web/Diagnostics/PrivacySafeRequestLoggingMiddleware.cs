using System.Diagnostics;

namespace JobTracker.Web.Diagnostics;

public sealed class PrivacySafeRequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<PrivacySafeRequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();
        context.Response.Headers["X-Request-ID"] = context.TraceIdentifier;

        try
        {
            await next(context);
        }
        finally
        {
            var endpoint = context.GetEndpoint()?.DisplayName ?? "unmatched endpoint";
            var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

            using var scope = logger.BeginScope(new Dictionary<string, object>
            {
                ["RequestId"] = context.TraceIdentifier,
            });

            logger.Log(
                context.Response.StatusCode >= StatusCodes.Status500InternalServerError
                    ? LogLevel.Warning
                    : LogLevel.Information,
                new EventId(1001, "RequestCompleted"),
                "HTTP {Method} {Endpoint} completed with {StatusCode} in {ElapsedMilliseconds:F1} ms",
                context.Request.Method,
                endpoint,
                context.Response.StatusCode,
                elapsedMilliseconds);
        }
    }
}
