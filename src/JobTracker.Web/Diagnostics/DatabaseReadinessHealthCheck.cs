using JobTracker.Web.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JobTracker.Web.Diagnostics;

public sealed class DatabaseReadinessHealthCheck(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseReadinessHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await database.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                eventId: new EventId(2001, "DatabaseReadinessFailed"),
                exception,
                "Database readiness check failed. Trace details are retained server-side only.");
            return HealthCheckResult.Unhealthy();
        }
    }
}
