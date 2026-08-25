using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Web.Applications;

public sealed record CompanyListItem(
    Guid Id,
    string Name,
    string? Location,
    string? Website,
    int ApplicationCount);

public sealed record CompanyDetails(
    Guid Id,
    string Name,
    string? Location,
    string? Website,
    string? Notes,
    IReadOnlyList<ApplicationListItem> Applications);

public sealed record CompanyInput(
    string Name,
    string? Location,
    string? Website,
    string? Notes);

public enum CompanyWriteResult
{
    Success,
    NotFound,
    Duplicate,
    InUse,
}

public sealed class CompanyTrackerService(
    ApplicationDbContext database,
    ICurrentUserContext currentUser)
{
    public async Task<IReadOnlyList<CompanyListItem>> SearchAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var query = database.Companies
            .AsNoTracking()
            .Where(company => company.OwnerId == ownerId);
        var searchText = search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(company =>
                company.Name.ToLower().Contains(searchText)
                || (company.Location != null && company.Location.ToLower().Contains(searchText)));
        }

        return await query
            .OrderBy(company => company.Name)
            .ThenBy(company => company.Location)
            .Select(company => new CompanyListItem(
                company.Id,
                company.Name,
                company.Location,
                company.Website,
                database.JobApplications.Count(application =>
                    application.OwnerId == ownerId && application.CompanyId == company.Id)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CompanyListItem>> ListOptionsAsync(
        CancellationToken cancellationToken = default) =>
        await SearchAsync(null, cancellationToken);

    public async Task<CompanyDetails?> FindAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var company = await database.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && item.OwnerId == ownerId, cancellationToken);
        if (company is null)
        {
            return null;
        }

        var applications = await database.JobApplications
            .AsNoTracking()
            .Where(application => application.OwnerId == ownerId && application.CompanyId == id)
            .OrderByDescending(application => application.AppliedOn)
            .Select(application => new ApplicationListItem(
                application.Id,
                application.RoleTitle,
                company.Name,
                application.AppliedOn,
                application.Stage,
                application.Outcome,
                application.SourceUrl,
                application.ApplicationPortalUrl,
                application.IsSavedForever,
                application.DeletionScheduledAt))
            .ToListAsync(cancellationToken);

        return new CompanyDetails(
            company.Id,
            company.Name,
            company.Location,
            company.Website,
            company.Notes,
            applications);
    }

    public async Task<(CompanyWriteResult Result, Guid? Id)> CreateAsync(
        CompanyInput input,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var duplicateId = await FindDuplicateIdAsync(input, ownerId, null, cancellationToken);
        if (duplicateId is not null)
        {
            return (CompanyWriteResult.Duplicate, duplicateId);
        }

        var company = new Company
        {
            OwnerId = ownerId,
            Name = input.Name.Trim(),
            Location = NullIfWhiteSpace(input.Location),
            Website = NullIfWhiteSpace(input.Website),
            Notes = NullIfWhiteSpace(input.Notes),
        };
        database.Companies.Add(company);
        await database.SaveChangesAsync(cancellationToken);
        return (CompanyWriteResult.Success, company.Id);
    }

    public async Task<(CompanyWriteResult Result, Guid? DuplicateId)> UpdateAsync(
        Guid id,
        CompanyInput input,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var company = await database.Companies.SingleOrDefaultAsync(
            item => item.Id == id && item.OwnerId == ownerId,
            cancellationToken);
        if (company is null)
        {
            return (CompanyWriteResult.NotFound, null);
        }

        var duplicateId = await FindDuplicateIdAsync(input, ownerId, id, cancellationToken);
        if (duplicateId is not null)
        {
            return (CompanyWriteResult.Duplicate, duplicateId);
        }

        company.Name = input.Name.Trim();
        company.Location = NullIfWhiteSpace(input.Location);
        company.Website = NullIfWhiteSpace(input.Website);
        company.Notes = NullIfWhiteSpace(input.Notes);
        await database.SaveChangesAsync(cancellationToken);
        return (CompanyWriteResult.Success, null);
    }

    public async Task<CompanyWriteResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var company = await database.Companies.SingleOrDefaultAsync(
            item => item.Id == id && item.OwnerId == ownerId,
            cancellationToken);
        if (company is null)
        {
            return CompanyWriteResult.NotFound;
        }

        var isInUse = await database.JobApplications.AnyAsync(
            application => application.OwnerId == ownerId && application.CompanyId == id,
            cancellationToken);
        if (isInUse)
        {
            return CompanyWriteResult.InUse;
        }

        database.Companies.Remove(company);
        await database.SaveChangesAsync(cancellationToken);
        return CompanyWriteResult.Success;
    }

    private async Task<Guid?> FindDuplicateIdAsync(
        CompanyInput input,
        string ownerId,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        var name = input.Name.Trim().ToLowerInvariant();
        var location = NullIfWhiteSpace(input.Location)?.ToLowerInvariant() ?? string.Empty;
        return await database.Companies
            .Where(company => company.OwnerId == ownerId && company.Id != excludedId)
            .Where(company => company.Name.ToLower() == name)
            .Where(company => (company.Location ?? string.Empty).ToLower() == location)
            .OrderBy(company => company.Id)
            .Select(company => (Guid?)company.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private string RequireOwnerId() =>
        currentUser.UserId
        ?? throw new InvalidOperationException("An authenticated user is required.");

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
