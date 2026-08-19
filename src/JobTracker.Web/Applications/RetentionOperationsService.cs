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
    int RetentionMonths,
    int DeletionGraceDays,
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
        var preferences = await database.Users
            .AsNoTracking()
            .Where(user => user.Id == ownerId)
            .Select(user => new RetentionPreferences(
                user.TimeZoneId,
                user.RetentionMonths,
                user.DeletionGraceDays))
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
                preferences.TimeZoneId,
                preferences.RetentionMonths))
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
            preferences.TimeZoneId,
            preferences.RetentionMonths,
            preferences.DeletionGraceDays,
            states.Count(item => item.State == ApplicationRetentionState.Saved),
            states.Count(item => item.State == ApplicationRetentionState.Recent),
            states.Count(item => item.State == ApplicationRetentionState.Eligible),
            states.Count(item => item.State == ApplicationRetentionState.DeletionScheduled),
            lastRun);
    }

    public async Task<bool> UpdateSettingsAsync(
        int retentionMonths,
        int deletionGraceDays,
        CancellationToken cancellationToken = default)
    {
        if (!RetentionPolicy.IsAllowed(retentionMonths, deletionGraceDays))
        {
            return false;
        }

        var ownerId = currentUser.UserId
            ?? throw new InvalidOperationException("An authenticated user is required.");
        var user = await database.Users.SingleAsync(
            item => item.Id == ownerId,
            cancellationToken);
        if (user.RetentionMonths == retentionMonths
            && user.DeletionGraceDays == deletionGraceDays)
        {
            return true;
        }

        var now = timeProvider.GetUtcNow();
        var applications = await database.JobApplications
            .Where(application => application.OwnerId == ownerId)
            .ToListAsync(cancellationToken);

        user.RetentionMonths = retentionMonths;
        user.DeletionGraceDays = deletionGraceDays;
        foreach (var application in applications)
        {
            var previousSchedule = application.DeletionScheduledAt;
            application.DeletionScheduledAt = RetentionPolicy.ReconcileSchedule(
                application.AppliedOn,
                application.IsSavedForever,
                null,
                now,
                user.TimeZoneId,
                retentionMonths: retentionMonths,
                gracePeriodDays: deletionGraceDays);
            if (previousSchedule != application.DeletionScheduledAt)
            {
                application.DeletionWarningDismissedAt = null;
            }
        }

        await database.SaveChangesAsync(cancellationToken);
        return true;
    }
}
