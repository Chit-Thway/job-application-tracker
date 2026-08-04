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
    bool IsSavedForever);

public sealed record ApplicationDetails(
    Guid Id,
    Guid? CompanyId,
    string? CompanyName,
    string RoleTitle,
    DateOnly AppliedOn,
    PipelineStage Stage,
    ApplicationOutcome Outcome,
    string? SourceUrl,
    string? Notes,
    bool IsSavedForever,
    DateTimeOffset? DeletionScheduledAt);

public sealed record ApplicationInput(
    Guid? CompanyId,
    string RoleTitle,
    DateOnly AppliedOn,
    string? SourceUrl,
    string? Notes,
    bool IsSavedForever);

public enum ApplicationWriteResult
{
    Success,
    NotFound,
    InvalidCompany,
}

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
                application.IsSavedForever))
            .ToListAsync(cancellationToken);
    }

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
                Notes = NullIfWhiteSpace(input.Notes),
                IsSavedForever = input.IsSavedForever,
            };
            var history = new StatusHistory
            {
                OwnerId = ownerId,
                JobApplicationId = application.Id,
                NewStage = PipelineStage.Applied,
                NewOutcome = ApplicationOutcome.Active,
                EffectiveAt = timeProvider.GetUtcNow(),
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

        application.CompanyId = input.CompanyId;
        application.RoleTitle = input.RoleTitle.Trim();
        application.AppliedOn = input.AppliedOn;
        application.SourceUrl = NullIfWhiteSpace(input.SourceUrl);
        application.Notes = NullIfWhiteSpace(input.Notes);
        application.IsSavedForever = input.IsSavedForever;
        if (application.IsSavedForever)
        {
            application.DeletionScheduledAt = null;
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

        application.IsSavedForever = isSavedForever;
        if (isSavedForever)
        {
            application.DeletionScheduledAt = null;
        }

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

    private async Task<bool> IsValidCompanyAsync(
        Guid? companyId,
        string ownerId,
        CancellationToken cancellationToken)
    {
        return companyId is null || await database.Companies.AnyAsync(
            company => company.Id == companyId && company.OwnerId == ownerId,
            cancellationToken);
    }

    private string RequireOwnerId() =>
        currentUser.UserId
        ?? throw new InvalidOperationException("An authenticated user is required.");

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
