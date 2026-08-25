using System.Data;
using System.Text.Json;
using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace JobTracker.Web.Extraction;

public sealed record ExtractionDraftReview(
    Guid Id,
    ExtractionSourceType SourceType,
    string SourceText,
    ExtractedJobFields Fields,
    IReadOnlyDictionary<string, string> Evidence,
    IReadOnlyList<string> Warnings,
    DateTimeOffset ExpiresAt);

public sealed record CompanyReuseSuggestion(
    Guid Id,
    string Name,
    string? Location,
    CompanyNameMatchKind MatchKind);

public sealed record ExtractionReviewInput(
    string RoleTitle,
    string? CompanyName,
    string? CompanyLocation,
    Guid? ExistingCompanyId,
    DateOnly AppliedOn,
    string? WorkplaceMode,
    string? SourceUrl,
    string? ApplicationPortalUrl,
    string? SourceSite,
    string? JobReference,
    string? SalaryText,
    string? EmploymentType,
    DateOnly? ClosingDate,
    string? ContactName,
    string? ContactEmail,
    string? DescriptionText,
    string? Notes,
    bool IsSavedForever);

public sealed record ReviewedExtractionMetadata(
    string? WorkplaceMode,
    string? SourceSite,
    string? JobReference,
    string? SalaryText,
    string? EmploymentType,
    DateOnly? ClosingDate,
    string? ContactName,
    string? ContactEmail);

public enum ExtractionDraftResult
{
    Success,
    NotFound,
    Expired,
    InvalidCompanySelection,
}

public sealed record ExtractionCompletion(
    ExtractionDraftResult Result,
    Guid? ApplicationId);

public sealed class ExtractionDraftService(
    ApplicationDbContext database,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider,
    PastedJobTextExtractor extractor)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DraftLifetime = TimeSpan.FromHours(24);

    public async Task<Guid> CreatePastedTextDraftAsync(
        string sourceText,
        DateOnly defaultAppliedOn,
        CancellationToken cancellationToken = default)
    {
        var extraction = extractor.Extract(sourceText);
        return await CreateDraftAsync(
            ExtractionSourceType.PastedText,
            extraction,
            defaultAppliedOn,
            cancellationToken);
    }

    public Task<Guid> CreateUrlDraftAsync(
        PastedJobExtraction extraction,
        DateOnly defaultAppliedOn,
        CancellationToken cancellationToken = default) =>
        CreateDraftAsync(
            ExtractionSourceType.JobPostingUrl,
            extraction,
            defaultAppliedOn,
            cancellationToken);

    public Task<Guid> CreateBrowserExtensionDraftAsync(
        PastedJobExtraction extraction,
        DateOnly defaultAppliedOn,
        CancellationToken cancellationToken = default) =>
        CreateDraftAsync(
            ExtractionSourceType.BrowserExtension,
            extraction,
            defaultAppliedOn,
            cancellationToken);

    private async Task<Guid> CreateDraftAsync(
        ExtractionSourceType sourceType,
        PastedJobExtraction extraction,
        DateOnly defaultAppliedOn,
        CancellationToken cancellationToken)
    {
        var ownerId = RequireOwnerId();
        var now = timeProvider.GetUtcNow();
        var fields = extraction.Fields with
        {
            AppliedOn = extraction.Fields.AppliedOn ?? defaultAppliedOn,
        };

        var expiredDrafts = await database.ExtractionDrafts
            .Where(draft => draft.OwnerId == ownerId && draft.ExpiresAt <= now)
            .ToListAsync(cancellationToken);
        database.ExtractionDrafts.RemoveRange(expiredDrafts);

        var draft = new ExtractionDraft
        {
            OwnerId = ownerId,
            SourceType = sourceType,
            SourceText = extraction.OriginalText,
            NormalizedText = extraction.NormalizedText,
            ParsedFieldsJson = JsonSerializer.Serialize(fields, JsonOptions),
            EvidenceJson = JsonSerializer.Serialize(extraction.Evidence, JsonOptions),
            WarningsJson = JsonSerializer.Serialize(extraction.Warnings, JsonOptions),
            CreatedAt = now,
            ExpiresAt = now.Add(DraftLifetime),
        };
        database.ExtractionDrafts.Add(draft);
        await database.SaveChangesAsync(cancellationToken);
        return draft.Id;
    }

    public async Task<ExtractionDraftReview?> FindReviewAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var now = timeProvider.GetUtcNow();
        var draft = await database.ExtractionDrafts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == id && item.OwnerId == ownerId && item.ExpiresAt > now,
                cancellationToken);
        return draft is null ? null : ToReview(draft);
    }

    public async Task<ExtractionCompletion> CompleteAsync(
        Guid id,
        ExtractionReviewInput input,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var now = timeProvider.GetUtcNow();
        IDbContextTransaction? transaction = null;

        try
        {
            if (database.Database.IsRelational())
            {
                transaction = await database.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
            }

            var draft = await database.ExtractionDrafts.SingleOrDefaultAsync(
                item => item.Id == id && item.OwnerId == ownerId,
                cancellationToken);
            if (draft is null)
            {
                return new ExtractionCompletion(ExtractionDraftResult.NotFound, null);
            }

            if (draft.ExpiresAt <= now)
            {
                database.ExtractionDrafts.Remove(draft);
                await database.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return new ExtractionCompletion(ExtractionDraftResult.Expired, null);
            }

            Guid? companyId;
            if (input.ExistingCompanyId is { } existingCompanyId)
            {
                companyId = await database.Companies
                    .Where(company => company.Id == existingCompanyId && company.OwnerId == ownerId)
                    .Select(company => (Guid?)company.Id)
                    .SingleOrDefaultAsync(cancellationToken);
                if (companyId is null)
                {
                    return new ExtractionCompletion(
                        ExtractionDraftResult.InvalidCompanySelection,
                        null);
                }
            }
            else
            {
                companyId = CreateCompany(
                    ownerId,
                    input.CompanyName,
                    input.CompanyLocation);
            }
            var preferences = await database.Users
                .AsNoTracking()
                .Where(user => user.Id == ownerId)
                .Select(user => new RetentionPreferences(
                    user.TimeZoneId,
                    user.RetentionMonths,
                    user.DeletionGraceDays))
                .SingleAsync(cancellationToken);
            var metadata = new ReviewedExtractionMetadata(
                NullIfWhiteSpace(input.WorkplaceMode),
                NullIfWhiteSpace(input.SourceSite),
                NullIfWhiteSpace(input.JobReference),
                NullIfWhiteSpace(input.SalaryText),
                NullIfWhiteSpace(input.EmploymentType),
                input.ClosingDate,
                NullIfWhiteSpace(input.ContactName),
                NullIfWhiteSpace(input.ContactEmail));
            var application = new JobApplication
            {
                OwnerId = ownerId,
                CompanyId = companyId,
                RoleTitle = input.RoleTitle.Trim(),
                AppliedOn = input.AppliedOn,
                SourceUrl = NullIfWhiteSpace(input.SourceUrl),
                ApplicationPortalUrl = NullIfWhiteSpace(input.ApplicationPortalUrl),
                SourceText = draft.SourceText,
                DescriptionText = NullIfWhiteSpace(input.DescriptionText),
                ExtractionMetadataJson = JsonSerializer.Serialize(metadata, JsonOptions),
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
                Note = draft.SourceType switch
                {
                    ExtractionSourceType.JobPostingUrl =>
                        "Application created from a reviewed public job URL.",
                    ExtractionSourceType.BrowserExtension =>
                        "Application created from a reviewed browser capture.",
                    _ => "Application created from reviewed pasted text.",
                },
            };

            database.JobApplications.Add(application);
            database.StatusHistory.Add(history);
            database.ExtractionDrafts.Remove(draft);
            await database.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new ExtractionCompletion(ExtractionDraftResult.Success, application.Id);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<ExtractionDraftResult> CancelAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var draft = await database.ExtractionDrafts.SingleOrDefaultAsync(
            item => item.Id == id && item.OwnerId == ownerId,
            cancellationToken);
        if (draft is null)
        {
            return ExtractionDraftResult.NotFound;
        }

        database.ExtractionDrafts.Remove(draft);
        await database.SaveChangesAsync(cancellationToken);
        return ExtractionDraftResult.Success;
    }

    public async Task<IReadOnlyList<CompanyReuseSuggestion>> FindCompanySuggestionsAsync(
        string? extractedCompanyName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(extractedCompanyName))
        {
            return [];
        }

        var ownerId = RequireOwnerId();
        var companies = await database.Companies
            .AsNoTracking()
            .Where(company => company.OwnerId == ownerId)
            .Select(company => new { company.Id, company.Name, company.Location })
            .ToListAsync(cancellationToken);

        return companies
            .Select(company => new CompanyReuseSuggestion(
                company.Id,
                company.Name,
                company.Location,
                CompanyNameMatcher.Match(extractedCompanyName, company.Name)))
            .Where(suggestion => suggestion.MatchKind != CompanyNameMatchKind.None)
            .OrderByDescending(suggestion => suggestion.MatchKind)
            .ThenBy(suggestion => suggestion.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(suggestion => suggestion.Location, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    public static ReviewedExtractionMetadata? ReadMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReviewedExtractionMetadata>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Guid? CreateCompany(
        string ownerId,
        string? companyName,
        string? companyLocation)
    {
        var name = NullIfWhiteSpace(companyName);
        if (name is null)
        {
            return null;
        }

        var company = new Company
        {
            OwnerId = ownerId,
            Name = name,
            Location = NullIfWhiteSpace(companyLocation),
        };
        database.Companies.Add(company);
        return company.Id;
    }

    private ExtractionDraftReview ToReview(ExtractionDraft draft) => new(
        draft.Id,
        draft.SourceType,
        draft.SourceText,
        JsonSerializer.Deserialize<ExtractedJobFields>(draft.ParsedFieldsJson, JsonOptions)
            ?? throw new InvalidOperationException("The extraction draft fields are invalid."),
        JsonSerializer.Deserialize<Dictionary<string, string>>(draft.EvidenceJson, JsonOptions)
            ?? new Dictionary<string, string>(StringComparer.Ordinal),
        JsonSerializer.Deserialize<List<string>>(draft.WarningsJson, JsonOptions) ?? [],
        draft.ExpiresAt);

    private string RequireOwnerId() =>
        currentUser.UserId
        ?? throw new InvalidOperationException("An authenticated user is required.");

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
