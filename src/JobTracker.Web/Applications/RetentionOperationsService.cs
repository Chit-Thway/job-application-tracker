using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Web.Applications;

public sealed record RetentionRunSummary(
    DateTimeOffset CompletedAt,
    bool Succeeded,
    string? ErrorCode);

public sealed record RetentionSettingsSnapshot(
    string TimeZoneId,
    int SavedCount,
    int RecentCount,
    int EligibleCount,
    int DeletionScheduledCount,
    RetentionRunSummary? LastRun);

public sealed class RetentionOperationsService(
    ApplicationDbContext database,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider)
{
    public async Task<RetentionSettingsSnapshot> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.UserId
            ?? throw new InvalidOperationException("An authenticated user is required.");
        var timeZoneId = await database.Users
            .AsNoTracking()
            .Where(user => user.Id == ownerId)
            .Select(user => user.TimeZoneId)
            .SingleAsync(cancellationToken);
        var applications = await database.JobApplications
            .AsNoTracking()
            .Where(application => application.OwnerId == ownerId)
            .Select(application => new
            {
                application.AppliedOn,
                application.IsSavedForever,
                application.DeletionScheduledAt,
            })
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var states = applications
            .Select(application => RetentionPolicy.Assess(
                application.AppliedOn,
                application.IsSavedForever,
                application.DeletionScheduledAt,
                now,
                timeZoneId))
            .ToList();
        var lastRun = await database.RetentionRuns
            .AsNoTracking()
            .OrderByDescending(run => run.CompletedAt)
            .Select(run => new RetentionRunSummary(
                run.CompletedAt,
                run.Succeeded,
                run.ErrorCode))
            .FirstOrDefaultAsync(cancellationToken);

        return new RetentionSettingsSnapshot(
            timeZoneId,
            states.Count(item => item.State == ApplicationRetentionState.Saved),
            states.Count(item => item.State == ApplicationRetentionState.Recent),
            states.Count(item => item.State == ApplicationRetentionState.Eligible),
            states.Count(item => item.State == ApplicationRetentionState.DeletionScheduled),
            lastRun);
    }
}
