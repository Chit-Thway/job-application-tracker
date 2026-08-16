using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JobTracker.Web.Diagnostics;

public static class HealthResponseWriter
{
    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store, max-age=0";

        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new
            {
                status = report.Status.ToString(),
                checks = report.Entries
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new
                    {
                        name = entry.Key,
                        status = entry.Value.Status.ToString(),
                    }),
            },
            cancellationToken: context.RequestAborted);
    }
}
