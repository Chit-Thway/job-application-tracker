using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace JobTracker.Web.Applications;

public sealed record ApplicationSearch(
    string? Search,
    Guid? CompanyId,
    PipelineStage? Stage,
    ApplicationOutcome? Outcome,
    bool? IsSavedForever,
    bool? IsDeletionScheduled,
    DateOnly? AppliedFrom,
    DateOnly? AppliedTo,
    string Sort);

public sealed record ApplicationListItem(
    Guid Id,
    string RoleTitle,
    string? CompanyName,
    DateOnly AppliedOn,
    PipelineStage Stage,
    ApplicationOutcome Outcome,
    string? SourceUrl,
    string? ApplicationPortalUrl,
    bool IsSavedForever,
    DateTimeOffset? DeletionScheduledAt,
    DateTimeOffset? CreatedAt = null);

public sealed record ApplicationDetails(
    Guid Id,
    Guid? CompanyId,
    string? CompanyName,
    string RoleTitle,
    DateOnly AppliedOn,
    PipelineStage Stage,
    ApplicationOutcome Outcome,
    string? SourceUrl,
    string? ApplicationPortalUrl,
    string? SourceText,
    string? DescriptionText,
    string? ExtractionMetadataJson,
    string? Notes,
    bool IsSavedForever,
    DateTimeOffset? DeletionScheduledAt);

public sealed record ApplicationInput(
    Guid? CompanyId,
    string RoleTitle,
    DateOnly AppliedOn,
    string? SourceUrl,
    string? ApplicationPortalUrl,
    string? DescriptionText,
    string? Notes,
    bool IsSavedForever);

public enum ApplicationWriteResult
{
    Success,
    NotFound,
    InvalidCompany,
    InvalidRetentionState,
}

public sealed record BulkApplicationResult(int MatchedCount, int ChangedCount);

public sealed record BulkApplicationDeleteItem(
    Guid Id,
    string RoleTitle,
    string? CompanyName,
    DateOnly AppliedOn);

public sealed class ApplicationTrackerService(
    ApplicationDbContext database,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<ApplicationListItem>> SearchAsync(
        ApplicationSearch search,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var query = database.JobApplications
            .AsNoTracking()
            .Where(application => application.OwnerId == ownerId);

        var searchText = search.Search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(application =>
                application.RoleTitle.ToLower().Contains(searchText)
                || database.Companies.Any(company =>
                    company.Id == application.CompanyId
                    && company.OwnerId == ownerId
                    && company.Name.ToLower().Contains(searchText)));
        }

        if (search.CompanyId is not null)
        {
            query = query.Where(application => application.CompanyId == search.CompanyId);
        }

        if (search.Stage is not null)
        {
            query = query.Where(application => application.Stage == search.Stage);
        }

        if (search.Outcome is not null)
        {
            query = query.Where(application => application.Outcome == search.Outcome);
        }

        if (search.IsSavedForever is not null)
        {
            query = query.Where(application =>
                application.IsSavedForever == search.IsSavedForever);
        }

        if (search.IsDeletionScheduled is not null)
        {
            query = search.IsDeletionScheduled.Value
                ? query.Where(application =>
                    !application.IsSavedForever
                    && application.DeletionScheduledAt != null)
                : query.Where(application =>
                    application.IsSavedForever
                    || application.DeletionScheduledAt == null);
        }

        if (search.AppliedFrom is not null)
        {
            query = query.Where(application => application.AppliedOn >= search.AppliedFrom);
        }

        if (search.AppliedTo is not null)
        {
            query = query.Where(application => application.AppliedOn <= search.AppliedTo);
        }

        query = search.Sort switch
        {
            "oldest" => query.OrderBy(application => application.AppliedOn)
                .ThenBy(application => application.RoleTitle),
            "title" => query.OrderBy(application => application.RoleTitle)
                .ThenByDescending(application => application.AppliedOn),
            "company" => query.OrderBy(application => database.Companies
                    .Where(company => company.Id == application.CompanyId && company.OwnerId == ownerId)
                    .Select(company => company.Name)
                    .SingleOrDefault())
                .ThenBy(application => application.RoleTitle),
            _ => query.OrderByDescending(application => application.AppliedOn)
                .ThenByDescending(application => database.StatusHistory
                    .Where(history =>
                        history.OwnerId == ownerId
                        && history.JobApplicationId == application.Id)
                    .Min(history => (DateTimeOffset?)history.EffectiveAt))
                .ThenBy(application => application.RoleTitle),
        };

        return await query.Select(application => new ApplicationListItem(
                application.Id,
                application.RoleTitle,
                database.Companies
                    .Where(company => company.Id == application.CompanyId && company.OwnerId == ownerId)
                    .Select(company => company.Name)
                    .SingleOrDefault(),
                application.AppliedOn,
                application.Stage,
                application.Outcome,
                application.SourceUrl,
                application.ApplicationPortalUrl,
                application.IsSavedForever,
                application.DeletionScheduledAt,
                database.StatusHistory
                    .Where(history =>
                        history.OwnerId == ownerId
                        && history.JobApplicationId == application.Id)
                    .Min(history => (DateTimeOffset?)history.EffectiveAt)))
            .ToListAsync(cancellationToken);
    }

    public async Task<string> GetCurrentTimeZoneIdAsync(
        CancellationToken cancellationToken = default) =>
        (await GetRetentionPreferencesAsync(RequireOwnerId(), cancellationToken)).TimeZoneId;

    public async Task<ApplicationDetails?> FindAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        return await database.JobApplications
            .AsNoTracking()
            .Where(application => application.Id == id && application.OwnerId == ownerId)
            .Select(application => new ApplicationDetails(
                application.Id,
                application.CompanyId,
                database.Companies
                    .Where(company => company.Id == application.CompanyId && company.OwnerId == ownerId)
                    .Select(company => company.Name)
                    .SingleOrDefault(),
                application.RoleTitle,
                application.AppliedOn,
                application.Stage,
                application.Outcome,
                application.SourceUrl,
                application.ApplicationPortalUrl,
                application.SourceText,
                application.DescriptionText,
                application.ExtractionMetadataJson,
                application.Notes,
                application.IsSavedForever,
                application.DeletionScheduledAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<(ApplicationWriteResult Result, Guid? Id)> CreateAsync(
        ApplicationInput input,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        if (!await IsValidCompanyAsync(input.CompanyId, ownerId, cancellationToken))
        {
            return (ApplicationWriteResult.InvalidCompany, null);
        }

        var now = timeProvider.GetUtcNow();
        var preferences = await GetRetentionPreferencesAsync(ownerId, cancellationToken);

        IDbContextTransaction? transaction = null;
        try
        {
            if (database.Database.IsRelational())
            {
                transaction = await database.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
            }

            var application = new JobApplication
            {
                OwnerId = ownerId,
                CompanyId = input.CompanyId,
                RoleTitle = input.RoleTitle.Trim(),
                AppliedOn = input.AppliedOn,
                SourceUrl = NullIfWhiteSpace(input.SourceUrl),
                ApplicationPortalUrl = NullIfWhiteSpace(input.ApplicationPortalUrl),
                DescriptionText = NullIfWhiteSpace(input.DescriptionText),
                Notes = NullIfWhiteSpace(input.Notes),
                IsSavedForever = input.IsSavedForever,
                DeletionScheduledAt = RetentionPolicy.ReconcileSchedule(
                    input.AppliedOn,
                    input.IsSavedForever,
                    null,
                    now,
                    preferences.TimeZoneId,
                    retentionMonths: preferences.RetentionMonths,
                    gracePeriodDays: preferences.DeletionGraceDays),
            };
            var history = new StatusHistory
            {
                OwnerId = ownerId,
                JobApplicationId = application.Id,
                NewStage = PipelineStage.Applied,
                NewOutcome = ApplicationOutcome.Active,
                EffectiveAt = now,
                Note = "Application created.",
            };

            database.JobApplications.Add(application);
            database.StatusHistory.Add(history);
            await database.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return (ApplicationWriteResult.Success, application.Id);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<ApplicationWriteResult> UpdateAsync(
        Guid id,
        ApplicationInput input,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var application = await database.JobApplications.SingleOrDefaultAsync(
            item => item.Id == id && item.OwnerId == ownerId,
            cancellationToken);
        if (application is null)
        {
            return ApplicationWriteResult.NotFound;
        }

        if (!await IsValidCompanyAsync(input.CompanyId, ownerId, cancellationToken))
        {
            return ApplicationWriteResult.InvalidCompany;
        }

        var wasSavedForever = application.IsSavedForever;
        var now = timeProvider.GetUtcNow();
        var preferences = await GetRetentionPreferencesAsync(ownerId, cancellationToken);

        application.CompanyId = input.CompanyId;
        application.RoleTitle = input.RoleTitle.Trim();
        application.AppliedOn = input.AppliedOn;
        application.SourceUrl = NullIfWhiteSpace(input.SourceUrl);
        application.ApplicationPortalUrl = NullIfWhiteSpace(input.ApplicationPortalUrl);
        application.DescriptionText = NullIfWhiteSpace(input.DescriptionText);
        application.Notes = NullIfWhiteSpace(input.Notes);
        application.IsSavedForever = input.IsSavedForever;
        application.DeletionScheduledAt = RetentionPolicy.ReconcileSchedule(
            application.AppliedOn,
            application.IsSavedForever,
            application.DeletionScheduledAt,
            now,
            preferences.TimeZoneId,
            startFreshGracePeriod: wasSavedForever && !application.IsSavedForever,
            retentionMonths: preferences.RetentionMonths,
            gracePeriodDays: preferences.DeletionGraceDays);
        if (application.IsSavedForever || wasSavedForever != application.IsSavedForever)
        {
            application.DeletionWarningDismissedAt = null;
        }

        await database.SaveChangesAsync(cancellationToken);
        return ApplicationWriteResult.Success;
    }

    public async Task<ApplicationWriteResult> SetSavedForeverAsync(
        Guid id,
        bool isSavedForever,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var application = await database.JobApplications.SingleOrDefaultAsync(
            item => item.Id == id && item.OwnerId == ownerId,
            cancellationToken);
        if (application is null)
        {
            return ApplicationWriteResult.NotFound;
        }

        var wasSavedForever = application.IsSavedForever;
        var now = timeProvider.GetUtcNow();
        var preferences = await GetRetentionPreferencesAsync(ownerId, cancellationToken);
        application.IsSavedForever = isSavedForever;
        application.DeletionScheduledAt = RetentionPolicy.ReconcileSchedule(
            application.AppliedOn,
            application.IsSavedForever,
            application.DeletionScheduledAt,
            now,
            preferences.TimeZoneId,
            startFreshGracePeriod: wasSavedForever && !application.IsSavedForever,
            retentionMonths: preferences.RetentionMonths,
            gracePeriodDays: preferences.DeletionGraceDays);
        if (application.IsSavedForever || wasSavedForever != application.IsSavedForever)
        {
            application.DeletionWarningDismissedAt = null;
        }

        await database.SaveChangesAsync(cancellationToken);
        return ApplicationWriteResult.Success;
    }

    public async Task<ApplicationWriteResult> DismissDeletionWarningAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var application = await database.JobApplications.SingleOrDefaultAsync(
            item => item.Id == id && item.OwnerId == ownerId,
            cancellationToken);
        if (application is null)
        {
            return ApplicationWriteResult.NotFound;
        }

        if (application.IsSavedForever || application.DeletionScheduledAt is null)
        {
            return ApplicationWriteResult.InvalidRetentionState;
        }

        application.DeletionWarningDismissedAt = timeProvider.GetUtcNow();
        await database.SaveChangesAsync(cancellationToken);
        return ApplicationWriteResult.Success;
    }

    public async Task<ApplicationWriteResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var application = await database.JobApplications.SingleOrDefaultAsync(
            item => item.Id == id && item.OwnerId == ownerId,
            cancellationToken);
        if (application is null)
        {
            return ApplicationWriteResult.NotFound;
        }

        database.JobApplications.Remove(application);
        await database.SaveChangesAsync(cancellationToken);
        return ApplicationWriteResult.Success;
    }

    public async Task<IReadOnlyList<BulkApplicationDeleteItem>> FindSelectedAsync(
        IReadOnlyCollection<Guid> applicationIds,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var ids = NormalizeIds(applicationIds);
        return await database.JobApplications
            .AsNoTracking()
            .Where(application =>
                application.OwnerId == ownerId
                && ids.Contains(application.Id))
            .OrderBy(application => application.RoleTitle)
            .Select(application => new BulkApplicationDeleteItem(
                application.Id,
                application.RoleTitle,
                database.Companies
                    .Where(company =>
                        company.Id == application.CompanyId
                        && company.OwnerId == ownerId)
                    .Select(company => company.Name)
                    .SingleOrDefault(),
                application.AppliedOn))
            .ToListAsync(cancellationToken);
    }

    public async Task<BulkApplicationResult> BulkDeleteAsync(
        IReadOnlyCollection<Guid> applicationIds,
        CancellationToken cancellationToken = default)
    {
        var applications = await FindTrackedSelectedAsync(applicationIds, cancellationToken);
        database.JobApplications.RemoveRange(applications);
        await database.SaveChangesAsync(cancellationToken);
        return new BulkApplicationResult(applications.Count, applications.Count);
    }

    public async Task<BulkApplicationResult> BulkChangeStageAsync(
        IReadOnlyCollection<Guid> applicationIds,
        PipelineStage stage,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var applications = await FindTrackedSelectedAsync(applicationIds, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var changed = applications.Where(application => application.Stage != stage).ToList();
        foreach (var application in changed)
        {
            database.StatusHistory.Add(new StatusHistory
            {
                OwnerId = ownerId,
                JobApplicationId = application.Id,
                PreviousStage = application.Stage,
                NewStage = stage,
                EffectiveAt = now,
                Note = "Stage changed through a bulk application action.",
            });
            application.Stage = stage;
        }

        await database.SaveChangesAsync(cancellationToken);
        return new BulkApplicationResult(applications.Count, changed.Count);
    }

    public async Task<BulkApplicationResult> BulkSetSavedForeverAsync(
        IReadOnlyCollection<Guid> applicationIds,
        bool isSavedForever,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var applications = await FindTrackedSelectedAsync(applicationIds, cancellationToken);
        var changed = applications
            .Where(application => application.IsSavedForever != isSavedForever)
            .ToList();
        if (changed.Count == 0)
        {
            return new BulkApplicationResult(applications.Count, 0);
        }

        var now = timeProvider.GetUtcNow();
        var preferences = await GetRetentionPreferencesAsync(ownerId, cancellationToken);
        foreach (var application in changed)
        {
            var wasSavedForever = application.IsSavedForever;
            application.IsSavedForever = isSavedForever;
            application.DeletionScheduledAt = RetentionPolicy.ReconcileSchedule(
                application.AppliedOn,
                application.IsSavedForever,
                application.DeletionScheduledAt,
                now,
                preferences.TimeZoneId,
                startFreshGracePeriod: wasSavedForever && !application.IsSavedForever,
                retentionMonths: preferences.RetentionMonths,
                gracePeriodDays: preferences.DeletionGraceDays);
            application.DeletionWarningDismissedAt = null;
        }

        await database.SaveChangesAsync(cancellationToken);
        return new BulkApplicationResult(applications.Count, changed.Count);
    }

    public async Task<BulkApplicationResult> BulkAddNoteAsync(
        IReadOnlyCollection<Guid> applicationIds,
        string note,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var applications = await FindTrackedSelectedAsync(applicationIds, cancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var application in applications)
        {
            database.StatusHistory.Add(new StatusHistory
            {
                OwnerId = ownerId,
                JobApplicationId = application.Id,
                EffectiveAt = now,
                Note = note.Trim(),
            });
        }

        await database.SaveChangesAsync(cancellationToken);
        return new BulkApplicationResult(applications.Count, applications.Count);
    }

    private async Task<List<JobApplication>> FindTrackedSelectedAsync(
        IReadOnlyCollection<Guid> applicationIds,
        CancellationToken cancellationToken)
    {
        var ownerId = RequireOwnerId();
        var ids = NormalizeIds(applicationIds);
        return await database.JobApplications
            .Where(application =>
                application.OwnerId == ownerId
                && ids.Contains(application.Id))
            .ToListAsync(cancellationToken);
    }

    private static Guid[] NormalizeIds(IReadOnlyCollection<Guid> applicationIds) =>
        applicationIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(200)
            .ToArray();

    private async Task<bool> IsValidCompanyAsync(
        Guid? companyId,
        string ownerId,
        CancellationToken cancellationToken)
    {
        return companyId is null || await database.Companies.AnyAsync(
            company => company.Id == companyId && company.OwnerId == ownerId,
            cancellationToken);
    }

    private async Task<RetentionPreferences> GetRetentionPreferencesAsync(
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.Users
            .AsNoTracking()
            .Where(user => user.Id == ownerId)
            .Select(user => new RetentionPreferences(
                user.TimeZoneId,
                user.RetentionMonths,
                user.DeletionGraceDays))
            .SingleAsync(cancellationToken);

    private string RequireOwnerId() =>
        currentUser.UserId
        ?? throw new InvalidOperationException("An authenticated user is required.");

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
